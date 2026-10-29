"use strict";

const Customization = (() => {
  const PARTS = { Keycap: "키캡", Switch: "축", LED: "LED", Case: "케이스", Plate: "보강판", CustomImage: "커스텀 이미지" };
  const COLORS = { Keycap: "#38a9ff", Switch: "#ffad32", LED: "#b5a0f2", Case: "#67d7aa", Plate: "#f18eb6", CustomImage: "#e6d36f" };
  const NUMBERS = new Set(["schema_version", "sequence", "elapsed_seconds", "keycap_index", "start_elapsed_seconds", "end_elapsed_seconds", "duration_seconds", "focused_duration_seconds"]);
  const BOOLS = new Set(["apply_success", "value_changed", "is_complete"]);

  /** 원본 셀과 헤더를 받아 커스터마이징 전용 숫자·불리언·누락 상태를 분리해 반환한다. */
  function Decode(header, cells, index) {
    const row = { raw: {}, status: {}, sourceIndex: index, cells: cells.slice() };
    header.forEach((field, column) => {
      const raw = cells[column]; row.raw[field] = raw;
      const text = raw?.trim() || "";
      row[field] = text || null;
      row.status[field] = raw === undefined ? "missing" : text === "" ? "blank" : "valid";
      if (!text) return;
      if (NUMBERS.has(field)) {
        const valid = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i.test(text) && Number.isFinite(Number(text));
        row[field] = valid ? Number(text) : null;
        if (!valid) row.status[field] = "invalid";
      } else if (BOOLS.has(field)) {
        row[field] = text.toLowerCase() === "true" ? true : text.toLowerCase() === "false" ? false : null;
        if (row[field] === null) row.status[field] = "invalid";
      }
    });
    return row;
  }

  /** 숫자 배열을 합산하고 누락된 값이 있으면 미기록 상태인 null을 반환한다. */
  function Sum(values) { return values.every(Number.isFinite) ? values.reduce((sum, value) => sum + value, 0) : null; }
  /** 알려진 값의 평균·중앙값·대상 수를 반환하고 미기록을 0으로 취급하지 않는다. */
  function Describe(values) {
    const known = values.filter(Number.isFinite).slice().sort((a, b) => a - b);
    const middle = Math.floor(known.length / 2);
    return { mean: known.length ? Sum(known) / known.length : null,
      median: !known.length ? null : known.length % 2 ? known[middle] : (known[middle - 1] + known[middle]) / 2,
      count: known.length, missing: values.length - known.length };
  }
  /** 실제 대상에 성공 적용한 이미지 행을 판정하고 미리보기와 취소를 제외한다. */
  function IsImageApply(row) { return row.record_type === "ImageAction" && ["Apply", "Replace"].includes(row.element_id) && row.apply_success === true; }

  /** 선택 결과 플래그가 알려진 경우에만 성공·실제 변경·재선택 횟수를 반환한다. */
  function Selections(rows) {
    const selected = rows.filter(row => row.record_type === "ElementSelected");
    const successKnown = selected.every(row => typeof row.apply_success === "boolean");
    const changeKnown = selected.every(row => row.apply_success === false || (row.apply_success === true && typeof row.value_changed === "boolean"));
    return { attempts: selected.length,
      success: successKnown ? selected.filter(row => row.apply_success === true).length : null,
      failed: successKnown ? selected.filter(row => row.apply_success === false).length : null,
      changed: changeKnown ? selected.filter(row => row.apply_success === true && row.value_changed === true).length : null,
      reselected: changeKnown ? selected.filter(row => row.apply_success === true && row.value_changed === false).length : null };
  }

  /** CSV 원본과 독립된 플레이어·세션·편집 회차·항목 방문 모델을 구성한다. */
  function Build(text, name, id) {
    const records = Playtest.ParseCsv(text);
    if (records.length < 2) throw new Error("헤더와 데이터 행이 필요합니다.");
    const header = records.shift().map(field => field.trim().toLowerCase());
    if (new Set(header).size !== header.length || header.some(field => !field)) throw new Error("헤더가 비었거나 중복됩니다.");
    if (!["record_type", "customization_run_id", "session_id", "elapsed_seconds"].every(field => header.includes(field))) throw new Error("커스터마이징 필수 헤더가 없습니다.");
    const rows = records.map((cells, index) => Decode(header, cells, index + 2));
    const warnings = [];
    const width = rows.filter(row => row.cells.length !== header.length).length;
    const invalid = rows.filter(row => Object.values(row.status).includes("invalid")).length;
    if (width) warnings.push("열 수 불일치 " + width + "행 · 원본 보존");
    if (invalid) warnings.push("잘못된 숫자·불리언 " + invalid + "행 · 해당 값은 미기록 처리");
    const missing = ["tester_id", "keyboard_id", "part", "part_visit_id", "apply_success", "value_changed", "focused_duration_seconds", "is_complete"].filter(field => !header.includes(field));
    if (missing.length) warnings.push("미기록 열: " + missing.join(", "));
    if (rows.some(row => row.schema_version !== null && row.schema_version !== undefined && row.schema_version !== 1)) warnings.push("커스터마이징 v1 이외의 스키마 버전 포함 · 알려진 열만 해석");
    if (!header.includes("tester_id")) warnings.push("플레이어 식별자 미기록 · 사람 수로 해석할 수 없습니다.");
    const events = rows.slice().sort(Playtest.Order);
    const runs = [...Playtest.Group(events.filter(row => row.customization_run_id), row => JSON.stringify([row.session_id, row.customization_run_id])).entries()].map(([key, entries], index) => {
      const start = entries.find(row => row.record_type === "CustomizationStart") || null;
      const end = entries.findLast(row => row.record_type === "CustomizationEnd") || null;
      const context = start || entries[0];
      const snapshotBatches = [];
      let batch = null, previous = null;
      entries.forEach(row => {
        if (row.record_type !== "CustomizationSnapshot") { batch = null; previous = row; return; }
        if (batch?.reason === row.end_reason && batch.values.has(row.part)) batch.reliable = false;
        if (!batch || batch.reason !== row.end_reason || batch.values.has(row.part)) {
          batch = { reason: row.end_reason, values: new Map(), rows: [], preceding: previous, reliable: previous?.record_type !== 'CustomizationSnapshot' };
          snapshotBatches.push(batch);
        }
        if (!Number.isFinite(row.sequence) || !Number.isFinite(previous?.sequence) || row.sequence !== previous.sequence + 1) batch.reliable = false;
        batch.values.set(row.part, row); batch.rows.push(row); previous = row;
      });
      const snapshots = new Map();
      Playtest.Group(snapshotBatches, item => item.reason).forEach((items, reason) => {
        if (items.length === 1 && items[0].reliable) snapshots.set(reason, items[0].values);
        else warnings.push("편집 " + (index + 1) + "회차: " + reason + " 스냅샷 " + items.length + "묶음 · 합쳐서 해석하지 않음");
      });
      const visits = [...Playtest.Group(entries.filter(row => ["PartVisitStart", "PartVisitEnd"].includes(row.record_type)), row => row.part_visit_id || "unidentified-" + row.sourceIndex).values()].map(visitRows => {
        const visitStart = visitRows.find(row => row.record_type === "PartVisitStart") || null;
        const visitEnd = visitRows.findLast(row => row.record_type === "PartVisitEnd") || null;
        return { id: (visitStart || visitEnd).part_visit_id, part: (visitStart || visitEnd).part, start: visitStart, end: visitEnd,
          duration: visitEnd?.duration_seconds ?? null, focused: visitEnd?.focused_duration_seconds ?? null };
      });
      const run = { key: id + ":" + key, id: context.customization_run_id, index: index + 1, fileId: id, fileName: name,
        tester: context.tester_id, session: context.session_id, keyboard: context.keyboard_id, build: context.build_version,
        start, end, rows: entries, snapshots, snapshotBatches, visits, duration: end?.duration_seconds ?? null, focused: end?.focused_duration_seconds ?? null,
        complete: end?.is_complete === true && end.end_reason === "RightClickExit" };
      run.selected = entries.filter(row => row.record_type === "ElementSelected");
      run.imageApplies = entries.filter(IsImageApply);
      run.options = entries.filter(row => row.record_type === "AvailableOption");
      run.partOffered = new Set(run.options.map(row => row.part));
      if (!start || !end) warnings.push("편집 " + run.index + "회차: " + (!start ? "시작" : "종료") + " 행 누락");
      if (visits.some(visit => !visit.end)) warnings.push("편집 " + run.index + "회차: 미종료 방문 있음 · 시간 추정하지 않음");
      return run;
    });
    return { id, name, rawText: text, header, rows, events, warnings, runs };
  }

  /** 회차 최종 이미지 스냅샷을 집계하고 미기록과 명시적인 이미지 없음을 구분한다. */
  function Images(run) {
    const snapshot = run.complete ? run.snapshots.get("Final") : null;
    const rows = [...(snapshot?.values() || [])].filter(row => row.part?.startsWith("Image:"));
    const caseRow = rows.find(row => row.part === "Image:CaseWhole");
    const keycaps = rows.filter(row => row.part.startsWith("Image:Keycap:"));
    const expected = new Set(run.snapshotBatches.flatMap(batch => [...batch.values.keys()].filter(part => part?.startsWith('Image:Keycap:'))));
    run.rows.filter(row => row.record_type === 'ImageAction' && Number.isInteger(row.keycap_index) && row.keycap_index >= 0).forEach(row => expected.add('Image:Keycap:' + row.keycap_index));
    const indices = [...expected].map(part => Number(part.slice(13)));
    const coverage = keycaps.length > 0 && indices.every(Number.isInteger) && Math.max(...indices) + 1 === expected.size && [...expected].every(part => keycaps.some(row => row.part === part));
    const withImage = keycaps.filter(row => row.element_id !== null && row.element_id !== "None");
    const caseRetained = !caseRow || caseRow.element_id === null ? null : caseRow.element_id !== "None";
    const capCount = !coverage || keycaps.some(row => row.element_id === null) ? null : withImage.length;
    return { caseRetained, keycaps: capCount, canvas: withImage.filter(row => row.element_id.startsWith("Canvas:")).length,
      individual: withImage.filter(row => row.element_id.startsWith("Individual:")).length,
      retained: caseRetained === true || (capCount !== null && capCount > 0) ? true : caseRetained === false && capCount !== null ? false : null };
  }

  /** 선택 범위 회차의 여섯 항목별 클릭·방문·체류·접근률을 계산해 반환한다. */
  function Parts(runs) {
    return Object.keys(PARTS).map(part => {
      const events = runs.flatMap(run => run.rows.filter(row => row.part === part));
      const visits = runs.flatMap(run => run.visits.filter(visit => visit.part === part));
      const ended = visits.filter(visit => visit.end);
      const offered = runs.filter(run => run.partOffered.has(part));
      const accessed = offered.filter(run => run.visits.some(visit => visit.part === part));
      const perRun = runs.map(run => {
        const items = run.visits.filter(visit => visit.part === part);
        if (!items.length && !run.partOffered.has(part)) return null;
        return items.some(visit => !visit.end) ? null : Sum(items.map(visit => visit.focused));
      });
      return { part, open: events.filter(row => row.record_type === "PartAccess" && row.element_id === "Open").length,
        close: events.filter(row => row.record_type === "PartAccess" && row.element_id === "Close").length,
        visits: visits.filter(visit => visit.start).length, unclosed: visits.filter(visit => !visit.end).length,
        duration: visits.length && !ended.length ? null : Sum(ended.map(visit => visit.duration)), focused: visits.length && !ended.length ? null : Sum(ended.map(visit => visit.focused)),
        offered: offered.length, accessed: accessed.length, rate: offered.length ? accessed.length / offered.length : null,
        revisits: runs.reduce((sum, run) => sum + Math.max(0, run.visits.filter(visit => visit.part === part && visit.start).length - 1), 0),
        selected: events.filter(row => row.record_type === "ElementSelected").length, distribution: Describe(perRun) };
    });
  }

  /** 제공 요소와 선택을 빌드·항목별로 연결하고 선택 비중과 최종 유지 분모를 반환한다. */
  function Elements(runs, part) {
    const all = runs.flatMap(run => run.rows.filter(row => row.part === part && ["AvailableOption", "ElementSelected"].includes(row.record_type)));
    return [...Playtest.Group(all, row => JSON.stringify([row.build_version, row.element_id])).values()].map(rows => {
      const element = rows[0].element_id, build = rows[0].build_version;
      const buildRuns = runs.filter(run => run.build === build);
      const selected = rows.filter(row => row.record_type === "ElementSelected");
      const offered = buildRuns.filter(run => run.options.some(row => row.part === part && row.element_id === element));
      const eligible = offered.filter(run => run.complete && run.snapshots.get("Final")?.get(part)?.element_id !== null && run.snapshots.get("Final")?.has(part));
      const retained = eligible.filter(run => run.snapshots.get("Final").get(part).element_id === element);
      const denominator = all.filter(row => row.record_type === "ElementSelected" && row.build_version === build).length;
      const testers = new Set(buildRuns.flatMap(run => run.selected.filter(row => row.part === part && row.element_id === element).map(() => run.tester)).filter(Boolean));
      const offeredTesters = new Set(offered.map(run => run.tester).filter(Boolean));
      return { element, build, ...Selections(selected),
        share: denominator ? selected.length / denominator : null, denominator,
        offered: offered.length, eligible: eligible.length, retained: retained.length, finalRate: eligible.length ? retained.length / eligible.length : null,
        selectedTesters: testers.size, offeredTesters: offeredTesters.size };
    });
  }

  /** 이미지 기능 제공·성공 적용 회차와 완료 후 유지 상태를 분모와 함께 반환한다. */
  function ImageMetrics(runs) {
    const offered = runs.filter(run => run.partOffered.has("CustomImage"));
    const used = offered.filter(run => run.imageApplies.length);
    const completedUsed = runs.filter(run => run.complete && run.imageApplies.length);
    const known = completedUsed.filter(run => Images(run).retained !== null);
    return { offered: offered.length, used: used.length, usage: offered.length ? used.length / offered.length : null,
      completedUsed: completedUsed.length, known: known.length, retained: known.filter(run => Images(run).retained).length,
      retention: completedUsed.length && known.length === completedUsed.length ? known.filter(run => Images(run).retained).length / completedUsed.length : null };
  }

  const FINAL_PARTS = ['Keycap', 'Switch', 'Case', 'Plate', 'LED', 'LEDColor', 'LEDEnabled'];
  /** 회차의 순서 연속성과 성공 선택의 이전값 연결을 확인하여 파츠별 초기·최종 판정을 반환한다. */
  function FinalParts(run) {
    const initial = run.snapshots.get('Initial'), final = run.complete ? run.snapshots.get('Final') : null;
    const continuous = !!run.start && !!run.end && run.rows.every((row, i, rows) => row.status.apply_success !== 'missing' && row.status.value_changed !== 'missing' && Number.isFinite(row.sequence) && (!i || row.sequence === rows[i - 1].sequence + 1));
    return FINAL_PARTS.map(part => {
      const before = initial?.get(part)?.element_id ?? null, after = final?.get(part)?.element_id ?? null;
      const actions = run.rows.filter(row => part === 'LEDColor' || part === 'LEDEnabled'
        ? row.record_type === 'Adjustment' && row.part === 'LED' && row.element_id === (part === 'LEDColor' ? 'Color' : 'Power')
        : row.record_type === 'ElementSelected' && row.part === part);
      let current = before, away = false, reliable = continuous && before !== null;
      actions.forEach(row => {
        if (row.apply_success === false) return;
        const value = part === 'LEDColor' || part === 'LEDEnabled' ? row.details : row.element_id;
        if (row.apply_success !== true || value == null || typeof row.value_changed !== 'boolean') { reliable = false; return; }
        const previousValue = part === 'LEDEnabled' && row.previous_element_id == null && ['true', 'false'].includes(current) && value === (current === 'true' ? 'false' : 'true') ? current : row.previous_element_id;
        if (previousValue !== current || row.value_changed !== (current !== value)) reliable = false;
        if (row.value_changed && value !== before) away = true;
        current = value;
      });
      if (run.rows.some(row => row.record_type === 'Load' && row.apply_success !== false)) reliable = false;
      if (current !== after) reliable = false;
      const counts = (part === 'LEDColor' || part === 'LEDEnabled') ? {
        attempts: actions.length, success: actions.every(row => typeof row.apply_success === 'boolean') ? actions.filter(row => row.apply_success).length : null,
        changed: actions.every(row => row.apply_success === false || typeof row.value_changed === 'boolean') ? actions.filter(row => row.apply_success === true && row.value_changed).length : null,
        reselected: actions.every(row => row.apply_success === false || typeof row.value_changed === 'boolean') ? actions.filter(row => row.apply_success === true && row.value_changed === false).length : null
      } : Selections(actions);
      return { part, before, after, ...counts, status: before === null || after === null ? '판정 불가' : before !== after ? '변경 유지' : !reliable ? '판정 불가' : away ? '원복' : '초기 유지' };
    });
  }
  /** 이미지 식별자와 적용 방식을 별도로 읽으며 명시적 None과 미기록을 보존한다. */
  function ImageState(value) {
    if (value == null) return { value: null, mode: null, id: null };
    if (value === 'None') return { value, mode: 'None', id: null };
    const match = /^(Canvas|Individual):(.*)$/.exec(value);
    return { value, mode: match ? match[1] : '미기록', id: match ? match[2] : value };
  }
  /** 두 스냅샷에서 확인된 이미지 대상의 합집합을 비교하고 누락 대상은 판정하지 않는다. */
  function ImageChanges(initial, final) {
    const keys = new Set(['Image:CaseWhole', ...[...(initial?.keys() || []), ...(final?.keys() || [])].filter(part => part.startsWith('Image:'))]);
    return [...keys].sort((a, b) => a.localeCompare(b, undefined, { numeric: true })).map(part => {
      const before = ImageState(initial?.get(part)?.element_id), after = ImageState(final?.get(part)?.element_id);
      const status = before.value === null || after.value === null ? '판정 불가' : before.value === 'None' ? after.value === 'None' ? '이미지 없음 유지' : '이미지 추가'
        : after.value === 'None' ? '이미지 제거' : before.id === after.id ? '이미지 유지' : '이미지 교체';
      return { part, before, after, status, modeChanged: before.mode !== null && after.mode !== null && before.mode !== after.mode };
    });
  }
  /** 저장·복원 성공 여부를 집계하고 마지막 성공 저장 직후의 독립 스냅샷만 Final과 비교한다. */
  function Storage(run) {
    const stats = type => { const rows = run.rows.filter(row => row.record_type === type); return { attempts: rows.length, success: rows.filter(row => row.apply_success === true).length, failed: rows.filter(row => row.apply_success === false).length, unknown: rows.filter(row => row.apply_success == null).length }; };
    const last = run.rows.findLast(row => row.record_type === 'Save' && row.apply_success === true);
    const batches = run.snapshotBatches.filter(batch => batch.preceding === last && batch.reason === 'Save:' + (last?.element_id ?? '') && batch.reliable);
    const final = run.complete ? run.snapshots.get('Final') : null;
    const saved = batches.length === 1 ? batches[0].values : null;
    const keys = new Set([...FINAL_PARTS, ...[...(saved?.keys() || []), ...(final?.keys() || [])].filter(part => part.startsWith('Image:')), 'Image:CaseWhole']);
    if (![...keys].some(part => part.startsWith('Image:Keycap:'))) keys.add('Image:Keycap:미기록');
    const different = [], unknown = [];
    keys.forEach(part => { const a = saved?.get(part)?.element_id, b = final?.get(part)?.element_id; if (a == null || b == null) unknown.push(part); else if (a !== b) different.push(part); });
    return { save: stats('Save'), load: stats('Load'), last, different, unknown,
      status: !last ? stats('Save').attempts ? '성공 저장 기록 없음' : '저장 기록 없음' : !saved || !final ? '저장 성공, 상태 비교 불가' : different.length ? '저장 후 추가 변경 있음' : unknown.length ? '저장 성공, 상태 비교 불가' : '저장 후 변경 없음' };
  }
  /** 완료 회차의 빌드별 탐색과 최종 구성을 집계하고 각 비율의 독립 분모를 반환한다. */
  function FinalElements(runs) {
    const completed = runs.filter(run => run.complete), result = [];
    for (const part of FINAL_PARTS.slice(0, 5)) {
      for (const [build, items] of Playtest.Group(completed, run => run.build)) {
        const known = items.filter(run => run.snapshots.get('Final')?.get(part)?.element_id != null);
        const values = new Set(items.flatMap(run => [...run.options.filter(row => row.part === part).map(row => row.element_id), ...run.selected.filter(row => row.part === part).map(row => row.element_id), run.snapshots.get('Final')?.get(part)?.element_id]).filter(value => value != null));
        values.forEach(element => {
          const matching = run => run.selected.filter(row => row.part === part && row.element_id === element);
          const selected = items.filter(run => matching(run).length), successful = items.filter(run => matching(run).some(row => row.apply_success === true));
          const selectedKnown = successful.filter(run => known.includes(run)), retained = known.filter(run => run.snapshots.get('Final').get(part).element_id === element);
          result.push({ part, build, element, ...Selections(selected.flatMap(matching)), testers: new Set(selected.map(run => run.tester).filter(Boolean)).size, selected: selected.length,
            denominator: known.length, retained: retained.length, selectedKnown: selectedKnown.length, direct: retained.filter(run => successful.includes(run)).length,
            initial: retained.filter(run => FinalParts(run).find(item => item.part === part).status === '초기 유지').length,
            finalChanged: retained.filter(run => { const before = run.snapshots.get('Initial')?.get(part)?.element_id; return before != null && before !== element; }).length });
        });
      }
    }
    return result;
  }
  /** 완료 회차의 완전한 기본 조합과 확인 가능한 이미지 유형을 빌드별로 집계한다. */
  function Combinations(runs) {
    const completed = runs.filter(run => run.complete), basic = [], image = [];
    let excluded = 0;
    completed.forEach(run => {
      const values = FINAL_PARTS.slice(0, 5).map(part => run.snapshots.get('Final')?.get(part)?.element_id);
      if (values.some(value => value == null)) excluded++; else basic.push({ run, value: values.join(' + '), build: run.build });
      const state = Images(run);
      const type = state.caseRetained === null || state.keycaps === null ? '판정 불가' : state.caseRetained ? state.keycaps > 0 ? '케이스와 키캡' : '케이스만' : state.keycaps > 0 ? '키캡만' : '없음';
      image.push({ run, value: type, build: run.build });
    });
    const group = rows => [...Playtest.Group(rows, row => JSON.stringify([row.build, row.value])).values()].map(items => ({ value: items[0].value, build: items[0].build, count: items.length, testers: new Set(items.map(item => item.run.tester).filter(Boolean)).size, denominator: rows.filter(row => row.build === items[0].build).length })).sort((a, b) => b.count - a.count);
    return { basic: group(basic), image: group(image), excluded };
  }

  return { PARTS, COLORS, Decode, Sum, Describe, Build, Images, Parts, Elements, IsImageApply, ImageMetrics, Selections, FINAL_PARTS, FinalParts, ImageChanges, Storage, FinalElements, Combinations };
})();
