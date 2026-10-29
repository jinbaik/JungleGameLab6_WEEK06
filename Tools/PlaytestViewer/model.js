"use strict";

const Playtest = (() => {
  const TYPES = ["None", "Explosion", "Bonus", "Row", "Column", "All"];
  const NUMBERS = new Set(("sequence elapsed_seconds press_sequence held_seconds key_down_count interval_index combo start_elapsed_seconds end_elapsed_seconds duration_seconds down_count apm player_score enemy_score player_score_start player_score_end score_delta score_per_second score_per_minute schema_version round_index keyboard_spawn_index broken_key_down_count broken_down_count quantity offer_slot offer_generation price currency_before currency_after layout_row layout_column inventory_capacity inventory_total").split(" "));
  const BOOLEANS = new Set(["is_complete", "was_broken", "is_confirmed"]);

  /** CSV 문자열의 BOM·인용부호·줄바꿈을 해석하고 원본 셀 배열을 반환한다. */
  function ParseCsv(text) {
    const records = [];
    let row = [], cell = "", quoted = false, closed = false;
    const source = text.replace(/^\uFEFF/, "");
    for (let index = 0; index < source.length; index++) {
      const char = source[index];
      if (char === '"') {
        if (quoted && source[index + 1] === '"') { cell += '"'; index++; }
        else if (quoted) { quoted = false; closed = true; }
        else if (!cell.length && !closed) quoted = true;
        else throw new Error("CSV 인용부호 형식 오류 · 레코드 " + (records.length + 1));
      } else if (!quoted && char === ",") { row.push(cell); cell = ""; closed = false; }
      else if (!quoted && (char === "\n" || char === "\r")) {
        if (char === "\r" && source[index + 1] === "\n") index++;
        row.push(cell);
        if (row.some(value => value.trim().length)) records.push(row);
        row = []; cell = ""; closed = false;
      } else if (closed && !/\s/.test(char)) throw new Error("닫힌 인용부호 뒤에 잘못된 문자가 있습니다.");
      else if (!closed || quoted) cell += char;
    }
    if (quoted) throw new Error("CSV 인용부호가 닫히지 않았습니다.");
    row.push(cell);
    if (row.some(value => value.trim().length)) records.push(row);
    return records;
  }

  /** 헤더와 셀을 변환하여 원본값, 유효값, 결측·잘못된 값 상태를 함께 반환한다. */
  function Decode(header, cells, index) {
    const row = { raw: {}, status: {}, sourceIndex: index, cells: cells.slice() };
    header.forEach((name, column) => {
      const raw = cells[column];
      row.raw[name] = raw;
      row.status[name] = raw === undefined ? "missing" : raw.trim() === "" ? "blank" : "valid";
      row[name] = raw?.trim() || null;
      if (row[name] === null) return;
      if (NUMBERS.has(name)) {
        const valid = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i.test(row[name]) && Number.isFinite(Number(row[name]));
        row[name] = valid ? Number(row[name]) : null;
        if (!valid) row.status[name] = "invalid";
      } else if (BOOLEANS.has(name)) {
        const value = row[name].toLowerCase();
        row[name] = value === "true" ? true : value === "false" ? false : null;
        if (row[name] === null) row.status[name] = "invalid";
      }
    });
    return row;
  }

  /** 시간とsequenceを数値比較し、同一値の場合は原本行順でイベント順を확정する。 */
  function Order(a, b) {
    return (a.elapsed_seconds ?? Infinity) - (b.elapsed_seconds ?? Infinity) ||
      (a.sequence ?? Infinity) - (b.sequence ?? Infinity) || a.sourceIndex - b.sourceIndex;
  }

  /** 入力行のDownイベントのみを判定し、入力集計対象かどうかを返す。 */
  function IsDown(row) { return (!row.record_type || row.record_type === "KeyInput") && row.event?.toLowerCase() === "down"; }

  /** 配列を指定した識別子でまとめ、新しいMapを返す。 */
  function Group(rows, key) {
    const groups = new Map();
    rows.forEach(row => { const id = key(row); if (!groups.has(id)) groups.set(id, []); groups.get(id).push(row); });
    return groups;
  }

  /** 入力範囲の状態条件を受け取り、当該Downが含まれるかを返す。 */
  function Includes(row, scope) {
    if (scope === "all") return true;
    return ["Alive", "Broken"].includes(row.input_state) || (scope === "mapped" && row.input_state === "InputDisabled");
  }

  /** 入力と対象キーボードの実際の配列を結合し、一般・特殊・分類不能を返す。 */
  function InputType(row, dataset) {
    if (!Includes(row, "mapped")) return "unknown";
    const layout = dataset.layouts.get(row.session_id + ":" + row.keyboard_id)?.get(row.key);
    if (layout?.special_type === "None") return "normal";
    return TYPES.slice(1).includes(layout?.special_type) ? "special" : "unknown";
  }

  /** 元のDownとスキーマ機能を使用し、入力種別・再入力・状態別の集計値を返す。 */
  function Inputs(rows, dataset, scope = "playable") {
    const all = rows.filter(IsDown);
    const selected = all.filter(row => Includes(row, scope));
    const context = dataset.header.includes("input_state");
    const layout = dataset.header.includes("special_type");
    const counts = { normal: 0, special: 0, unknown: 0 };
    selected.forEach(row => counts[InputType(row, dataset)]++);
    return {
      total: context || scope === "all" ? selected.length : null,
      raw: all.length, normal: layout && context ? counts.normal : null, special: layout && context ? counts.special : null,
      unknown: layout && context ? counts.unknown : null,
      broken: dataset.header.includes("was_broken") ? selected.filter(row => row.was_broken === true).length : null,
      states: Group(all, row => row.input_state || "미수집"), selected
    };
  }

  /** 기믹행을effect_id로 묶어 파괴·발동·취소·미종결 대기를 중복 없이 집계한다. */
  function Effects(rows, owner) {
    const events = rows.filter(row => row.record_type === "SpecialKeycapEvent" && (!owner || row.owner === owner));
    return TYPES.slice(1).map(type => {
      const groups = Group(events.filter(row => row.special_type === type && row.effect_id), row => row.effect_id);
      const result = { type, Broken: 0, Activated: 0, Cancelled: 0, Skipped: 0, pending: 0 };
      groups.forEach(states => {
        const actions = new Set(states.map(row => row.action));
        ["Broken", "Activated", "Skipped"].forEach(action => { if (actions.has(action)) result[action]++; });
        if (actions.has("Cancelled") && !actions.has("Activated")) result.Cancelled++;
        if (actions.has("Queued") && !["Activated", "Skipped", "Cancelled"].some(action => actions.has(action))) result.pending++;
      });
      return result;
    });
  }

  /** 확정 구매·리롤의 재화 전후 차이를 합산하되 빈 숫자는 미수집으로 유지한다. */
  function Spend(rows) {
    const actions = rows.filter(row => row.record_type === "ShopAction" && ["PurchaseConfirmed", "Reroll"].includes(row.action));
    return actions.every(row => Number.isFinite(row.currency_before) && Number.isFinite(row.currency_after))
      ? actions.reduce((sum, row) => sum + row.currency_before - row.currency_after, 0) : null;
  }

  /** CSV 텍스트와 파일명을 읽어 원본 행과 독립적인 실행·대전·키보드·상점 모델을 반환한다. */
  function Build(text, name, id) {
    const records = ParseCsv(text);
    if (records.length < 2) throw new Error("헤더와 데이터 행이 필요합니다.");
    const header = records[0].map(value => value.trim().toLowerCase());
    if (new Set(header).size !== header.length || header.some(value => !value)) throw new Error("헤더 이름이 비었거나 중복됩니다.");
    if (!["elapsed_seconds", "key", "event"].every(field => header.includes(field))) throw new Error("플레이테스트 필수 헤더(elapsed_seconds, key, event)가 없습니다.");
    const rows = records.slice(1).map((cells, index) => Decode(header, cells, index + 2));
    const warnings = [];
    const widthErrors = rows.filter(row => row.cells.length !== header.length);
    if (widthErrors.length) warnings.push("열 수가 다른 행 " + widthErrors.length + "개 · 원본 행을 보존했습니다.");
    const invalid = rows.filter(row => Object.values(row.status).includes("invalid"));
    if (invalid.length) warnings.push("잘못된 숫자·불리언을 포함한 행 " + invalid.length + "개 · 합계에서 제외됩니다.");
    const sequences = Group(rows.filter(row => Number.isFinite(row.sequence)), row => JSON.stringify([row.session_id, row.sequence]));
    const duplicates = [...sequences.values()].filter(entries => entries.length > 1).length;
    if (duplicates) warnings.push("중복 sequence " + duplicates + "개 · 원본을 보존하고 진단을 위해 별도로 표시합니다.");
    const events = rows.slice().sort(Order);
    const dataset = { id, name, header, rows, events, rawText: text, warnings, layouts: new Map(), runs: [], tournaments: [], keyboards: [], shops: [], snapshots: [] };
    const versions = [...new Set(rows.map(row => row.schema_version).filter(Number.isFinite))];
    dataset.version = versions.length ? versions.join(", ") : "1 (구형)";
    if (!header.includes("special_type")) warnings.push("이 스키마는 기믹 배치·보유·발동을 수집하지 않습니다.");
    if (events.some(row => !Number.isFinite(row.elapsed_seconds))) warnings.push("시각 미수집·오류 행은 타임라인 끝에 배치됩니다.");
    events.filter(row => row.record_type === "KeyboardLayout").forEach(row => {
      const key = row.session_id + ":" + row.keyboard_id;
      if (!dataset.layouts.has(key)) dataset.layouts.set(key, new Map());
      if (dataset.layouts.get(key).has(row.key)) warnings.push("중복 배치: " + row.keyboard_id + " / " + row.key);
      dataset.layouts.get(key).set(row.key, row);
    });
    const stageRows = Group(events.filter(row => row.stage_run_id), row => row.session_id + ":" + row.stage_run_id);
    stageRows.forEach((entries, key) => {
      const start = entries.find(row => row.record_type === "StageStart") || null;
      const end = entries.findLast(row => row.record_type === "StageEnd") || null;
      const context = start || entries[0];
      const run = { key, id: context.stage_run_id, session: context.session_id ?? null, tournament: context.tournament_run_id ?? null,
        round: context.round_index, kind: context.battle_kind || context.stage_id?.split(/[\/\\]/).at(-1) || null,
        start, end, rows: entries, complete: end?.is_complete === true,
        duration: end?.duration_seconds ?? null, player: end?.player_score ?? null, enemy: end?.enemy_score ?? null };
      run.outcome = !run.complete ? "미완료" : !Number.isFinite(run.player) || !Number.isFinite(run.enemy) ? "승패 미수집"
        : run.player > run.enemy ? "승리" : run.player < run.enemy ? "패배" : "무승부";
      run.inputs = Inputs(entries, dataset);
      dataset.runs.push(run);
      if (!start || !end) warnings.push("대전 " + (run.round ?? run.id) + ": " + (!start ? "시작" : "종료") + " 행 없음");
      const broken = Inputs(entries, dataset, "all").broken;
      if (Number.isFinite(end?.broken_down_count) && broken !== end.broken_down_count) warnings.push("재입력 요약 불일치: " + run.id);
    });
    if (!dataset.runs.length && events.some(IsDown)) {
      dataset.runs.push({ key: "legacy", id: "대전 ID 미수집", session: events[0].session_id, tournament: null, round: null,
        kind: null, start: null, end: null, rows: events, complete: false, duration: null, player: null, enemy: null, outcome: "대전 구분 미수집", inputs: Inputs(events, dataset) });
    }
    Group(events, row => JSON.stringify([row.session_id ?? null, row.tournament_run_id ?? null])).forEach((entries, key) => {
      if (!entries.some(row => row.tournament_run_id) && !entries.some(row => row.stage_run_id || IsDown(row))) return;
      const context = entries[0];
      const runs = dataset.runs.filter(run => run.session === (context.session_id ?? null) && run.tournament === (context.tournament_run_id ?? null));
      dataset.tournaments.push({ key, session: context.session_id ?? null, id: context.tournament_run_id ?? null, rows: entries, runs });
    });
    const keyboardRows = Group(events.filter(row => row.keyboard_id), row => row.session_id + ":" + row.keyboard_id);
    keyboardRows.forEach((entries, key) => {
      const spawn = entries.find(row => row.record_type === "KeyboardSpawn") || null;
      const context = spawn || entries[0];
      dataset.keyboards.push({ key, id: context.keyboard_id, spawn, owner: context.owner, run: context.session_id + ":" + context.stage_run_id,
        rows: entries, layout: dataset.layouts.get(key) || new Map(), inputs: Inputs(entries, dataset) });
    });
    Group(events.filter(row => row.shop_visit_id), row => row.session_id + ":" + row.shop_visit_id).forEach((entries, key) => {
      const start = entries.find(row => row.record_type === "ShopStart") || null;
      const end = entries.findLast(row => row.record_type === "ShopEnd") || null;
      const context = start || entries[0];
      const actions = entries.filter(row => row.record_type === "ShopAction");
      dataset.shops.push({ key, id: context.shop_visit_id, session: context.session_id, tournament: context.tournament_run_id, round: context.round_index,
        start, end, actions, duration: end?.duration_seconds ?? null,
        complete: end?.is_complete === true && end.end_reason === "NextBattle",
        offers: Group(actions.filter(row => row.action === "OfferPresented"), row => JSON.stringify([row.offer_generation, row.offer_slot])),
        transactions: Group(actions.filter(row => row.shop_transaction_id), row => row.shop_transaction_id) });
    });
    Group(events.filter(row => row.record_type === "InventorySnapshot"), row => JSON.stringify([row.session_id, row.snapshot_id, row.owner])).forEach(entries => {
      dataset.snapshots.push({ context: entries[0], rows: entries, quantities: Object.fromEntries(entries.map(row => [row.special_type, row.quantity])) });
    });
    return dataset;
  }

  /** 대전 입력의 실제 관측 시간축과 선택 구간 크기로 Down 분포를 반환한다. */
  function Buckets(run, dataset, scope, seconds) {
    const start = run.start?.elapsed_seconds;
    if (!Number.isFinite(start)) return [];
    const times = run.rows.map(row => row.elapsed_seconds).filter(Number.isFinite);
    const end = run.end?.elapsed_seconds ?? times.reduce((max, value) => Math.max(max, value), start);
    const count = Math.min(1000, Math.max(1, Math.ceil((end - start) / seconds)));
    const width = Math.max(seconds, (end - start) / count);
    const buckets = Array.from({ length: count }, (_, index) => ({ start: index * width, end: Math.min(end - start, (index + 1) * width), count: 0 }));
    Inputs(run.rows, dataset, scope).selected.forEach(row => {
      if (Number.isFinite(row.elapsed_seconds)) {
        const index = Math.min(count - 1, Math.floor((row.elapsed_seconds - start) / width));
        if (index >= 0) buckets[index].count++;
      }
    });
    return buckets;
  }

  /** 대전의 실제 점수 스냅샷과 구간 APM을 상대 시각으로 변환한 차트 모델을 반환한다. */
  function Scores(run) {
    const start = run.start?.elapsed_seconds;
    if (!Number.isFinite(start)) return { player: [], enemy: [], intervals: [], apm: [] };
    const player = run.rows.filter(row => ["StageStart", "ScoreSnapshot", "StageEnd"].includes(row.record_type) && row.owner !== "Enemy" &&
      Number.isFinite(row.player_score) && Number.isFinite(row.elapsed_seconds)).map(row => ({ x: row.elapsed_seconds - start, y: row.player_score }));
    const enemy = run.rows.filter(row => row.record_type === "ScoreSnapshot" && Number.isFinite(row.enemy_score) && Number.isFinite(row.elapsed_seconds))
      .map(row => ({ x: row.elapsed_seconds - start, y: row.enemy_score }));
    const intervals = run.rows.filter(row => row.record_type === "IntervalSummary");
    const apm = []; let previous = null;
    intervals.forEach(row => {
      if (!Number.isFinite(row.start_elapsed_seconds) || !Number.isFinite(row.end_elapsed_seconds) || !Number.isFinite(row.apm)) return;
      if (previous !== null && Math.abs(previous - row.start_elapsed_seconds) > 0.000001) apm.push({ x: null, y: null });
      apm.push({ x: row.start_elapsed_seconds - start, y: row.apm }, { x: row.end_elapsed_seconds - start, y: row.apm });
      previous = row.end_elapsed_seconds;
    });
    return { player, enemy, intervals, apm };
  }

  /** 대전 시작과 플레이어 보충 입장 행을 사용하여 중복 없는 상대 시각 표시점을 반환한다. */
  function ReplacementMarkers(run) {
    const start = run.start?.elapsed_seconds;
    if (!Number.isFinite(start)) return [];
    const rows = run.rows.filter(row => row.record_type === "KeyboardSpawn" && row.owner === "Player" &&
      row.spawn_kind === "Replacement" && Number.isFinite(row.elapsed_seconds) && row.elapsed_seconds >= start &&
      (!Number.isFinite(run.end?.elapsed_seconds) || row.elapsed_seconds <= run.end.elapsed_seconds));
    return [...Group(rows, row => row.keyboard_id || row.sourceIndex).values()].map(entries => {
      const row = entries[0];
      return { x: row.elapsed_seconds - start, label: "키보드 " + (row.keyboard_spawn_index ?? "번호 미수집") + " 보충" };
    });
  }

  /** 거래 후보와 확정 행을 분리하여 확정 수량·지출·연결 스냅샷 및 완료 상태를 반환한다. */
  function Transaction(entries) {
    const confirmed = entries.filter(row => row.action === "PurchaseConfirmed");
    const cancelled = entries.some(row => row.action === "PurchaseCancelled");
    const discards = entries.filter(row => row.action === "DiscardConfirmed");
    const quantity = confirmed.every(row => Number.isFinite(row.quantity)) ? confirmed.reduce((sum, row) => sum + row.quantity, 0) : null;
    const discarded = discards.every(row => Number.isFinite(row.quantity)) ? discards.reduce((sum, row) => sum + row.quantity, 0) : null;
    return { quantity, discarded, spend: Spend(confirmed), snapshotId: confirmed.at(-1)?.snapshot_id,
      outcome: confirmed.length ? "구매 확정" : cancelled ? "취소" : "미완료" };
  }

  /** 선택한 토너먼트의 완료 조건과 입력 범위에 맞춰 시간·입력률·보유 비교 모델을 반환한다. */
  function Comparison(dataset, tournament, scope, includeIncomplete) {
    const runs = tournament.runs.filter(run => includeIncomplete || run.complete);
    const input = Inputs(runs.flatMap(run => run.rows), dataset, scope);
    const durations = runs.map(run => run.duration);
    const duration = durations.length && durations.every(Number.isFinite) ? durations.reduce((sum, value) => sum + value, 0) : null;
    const shops = dataset.shops.filter(shop => shop.session === tournament.session && shop.tournament === tournament.id);
    const shopTimes = shops.map(shop => shop.duration);
    const shopTime = !shops.length ? dataset.header.includes("shop_visit_id") ? 0 : null
      : shopTimes.every(Number.isFinite) ? shopTimes.reduce((sum, value) => sum + value, 0) : null;
    const final = dataset.snapshots.filter(snapshot => snapshot.context.tournament_run_id === tournament.id &&
      snapshot.context.session_id === tournament.session && snapshot.context.owner === "Player").at(-1);
    return { runs, input, duration, shopTime, final, perMinute: duration > 0 && input.total !== null ? input.total * 60 / duration : null };
  }

  return { TYPES, ParseCsv, Decode, Build, Order, IsDown, Group, Includes, Inputs, InputType, Effects, Spend, Buckets, Scores, ReplacementMarkers, Transaction, Comparison };
})();

