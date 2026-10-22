"use strict";

const Customization = (() => {
  const PARTS = { Keycap: "키캡", Switch: "축", LED: "LED", Case: "케이스", Plate: "보강판", CustomImage: "커스텀 이미지" };
  const ELEMENT_PARTS = { Keycap: "키캡", Switch: "축", LED: "LED", Case: "케이스", Plate: "보강판 재질", PlateEffect: "보강판 효과", PlateMaterial: "보강판 재질", CustomImage: "커스텀 이미지" };
  const COLORS = { Keycap: "#38a9ff", Switch: "#ffad32", LED: "#b5a0f2", Case: "#67d7aa", Plate: "#f18eb6", PlateEffect: "#f18eb6", PlateMaterial: "#f18eb6", CustomImage: "#e6d36f" };
  const NUMBERS = new Set(["schema_version", "sequence", "elapsed_seconds", "keycap_index", "start_elapsed_seconds", "end_elapsed_seconds", "duration_seconds", "focused_duration_seconds"]);
  const BOOLS = new Set(["apply_success", "value_changed", "is_complete", "is_installed"]);

  /** 보강판 재질(Plate·PlateMaterial)의 기록 열 이름을 동일 파츠로 매칭한다. */
  function SameElementPart(rowPart, part) {
    return part === "Plate" ? rowPart === "Plate" || rowPart === "PlateMaterial" : rowPart === part;
  }

  /** 상단 6개 탭 기준으로 보강판 탭(Plate·PlateMaterial·PlateEffect) 소속 여부를 반환한다. */
  function SameTabPart(rowPart, tabPart) {
    return tabPart === "Plate" ? rowPart === "Plate" || rowPart === "PlateMaterial" || rowPart === "PlateEffect" : rowPart === tabPart;
  }

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
      } else if (field === 'keycap_kind' && !['Regular', 'Artisan'].includes(text)) {
        row[field] = null; row.status[field] = 'invalid';
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
    return {
      mean: known.length ? Sum(known) / known.length : null,
      median: !known.length ? null : known.length % 2 ? known[middle] : (known[middle - 1] + known[middle]) / 2,
      count: known.length, missing: values.length - known.length
    };
  }
  /** 실제 대상에 성공 적용한 이미지 행을 판정하고 미리보기와 취소를 제외한다. */
  function IsImageApply(row) { return row.record_type === "ImageAction" && ["Apply", "Replace"].includes(row.element_id) && row.apply_success === true; }

  /** 선택 결과 플래그가 알려진 경우에만 성공·실제 변경·재선택 횟수를 반환한다. */
  function Selections(rows) {
    const selected = rows.filter(row => ["ElementSelected", "KeycapApply"].includes(row.record_type));
    const successKnown = selected.every(row => typeof row.apply_success === "boolean");
    const changeKnown = selected.every(row => row.apply_success === false || (row.apply_success === true && typeof row.value_changed === "boolean"));
    return {
      attempts: selected.length,
      success: successKnown ? selected.filter(row => row.apply_success === true).length : null,
      failed: successKnown ? selected.filter(row => row.apply_success === false).length : null,
      changed: changeKnown ? selected.filter(row => row.apply_success === true && row.value_changed === true).length : null,
      reselected: changeKnown ? selected.filter(row => row.apply_success === true && row.value_changed === false).length : null
    };
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
    if (rows.some(row => row.schema_version != null && ![1, 2, 3].includes(row.schema_version))) warnings.push("지원 범위 v1/v2/v3 이외 스키마 포함 · 알려진 열만 해석");
    if (rows.some(row => row.schema_version >= 2)) {
      const absent = ['keycap_kind', 'material_id', 'snapshot_id', 'is_installed'].filter(field => !header.includes(field));
      if (absent.length) warnings.push('v2+ 미기록 열: ' + absent.join(', '));
    }
    const events = rows.slice().sort(Playtest.Order);
    const runs = [...Playtest.Group(events.filter(row => row.customization_run_id), row => JSON.stringify([row.session_id, row.customization_run_id])).entries()].map(([key, entries], index) => {
      const start = entries.find(row => row.record_type === "CustomizationStart") || null;
      const end = entries.findLast(row => row.record_type === "CustomizationEnd") || null;
      const context = start || entries[0];
      const snapshotBatches = [];
      const identifiedBatches = new Map();
      let batch = null, previous = null;
      entries.forEach(row => {
        if (row.record_type !== "CustomizationSnapshot") { batch = null; previous = row; return; }
        if (row.schema_version >= 2) {
          const id = row.snapshot_id;
          if (!id) {
            snapshotBatches.push({ id: null, reason: row.end_reason, values: new Map(), rows: [row], preceding: previous, reliable: false });
            previous = row; batch = null; return;
          }
          let identified = identifiedBatches.get(id);
          if (!identified) {
            identified = { id, reason: row.end_reason, values: new Map(), rows: [], preceding: previous, reliable: true };
            identifiedBatches.set(id, identified); snapshotBatches.push(identified);
          }
          const index = KeyIndex(row), part = index !== null ? 'Keycap:' + index : row.part;
          if (row.part?.startsWith('Keycap:') && index === null || identified.rows.length && previous?.snapshot_id !== id) identified.reliable = false;
          if (!part || identified.values.has(part) || identified.reason !== row.end_reason) identified.reliable = false;
          if (!Number.isFinite(row.sequence) || !Number.isFinite(previous?.sequence) || row.sequence !== previous.sequence + 1) identified.reliable = false;
          identified.values.set(part, row); identified.rows.push(row); previous = row; batch = null; return;
        }
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
        else if (!items.every(item => item.id && item.reliable) || ['Initial', 'Final', 'Interrupted'].includes(reason)) warnings.push("편집 " + (index + 1) + "회차: " + reason + " 스냅샷 " + items.length + "묶음 · 합쳐서 해석하지 않음");
      });
      const visits = [...Playtest.Group(entries.filter(row => ["PartVisitStart", "PartVisitEnd"].includes(row.record_type)), row => row.part_visit_id || "unidentified-" + row.sourceIndex).values()].map(visitRows => {
        const visitStart = visitRows.find(row => row.record_type === "PartVisitStart") || null;
        const visitEnd = visitRows.findLast(row => row.record_type === "PartVisitEnd") || null;
        return {
          id: (visitStart || visitEnd).part_visit_id, part: (visitStart || visitEnd).part, start: visitStart, end: visitEnd,
          duration: visitEnd?.duration_seconds ?? null, focused: visitEnd?.focused_duration_seconds ?? null
        };
      });
      const run = {
        key: id + ":" + key, id: context.customization_run_id, index: index + 1, fileId: id, fileName: name,
        tester: id, rawTester: context.tester_id, session: context.session_id, keyboard: context.keyboard_id, build: context.build_version,
        version: Math.max(1, ...entries.map(row => row.schema_version).filter(Number.isFinite)),
        start, end, rows: entries, snapshots, snapshotBatches, visits, duration: end?.duration_seconds ?? null, focused: end?.focused_duration_seconds ?? null,
        complete: end?.is_complete === true && ["RightClickExit", "NextScene"].includes(end.end_reason)
      };
      run.selected = entries.filter(row => row.record_type === 'KeycapApply' && row.schema_version >= 2 || row.record_type === 'ElementSelected' && !(row.part === 'Keycap' && row.schema_version >= 2));
      run.picked = entries.filter(row => row.record_type === 'ElementPicked' && row.part === 'Keycap' && row.schema_version >= 2);
      run.imageApplies = entries.filter(IsImageApply);
      run.options = entries.filter(row => row.record_type === "AvailableOption");
      run.partOffered = new Set(run.options.map(row => row.part === "PlateMaterial" || row.part === "PlateEffect" ? "Plate" : row.part));
      if (!start || !end) warnings.push("편집 " + run.index + "회차: " + (!start ? "시작" : "종료") + " 행 누락");
      if (visits.some(visit => !visit.end)) warnings.push("편집 " + run.index + "회차: 미종료 방문 있음 · 시간 추정하지 않음");
      return run;
    });
    return { id, name, rawText: text, header, rows, events, warnings, runs };
  }

  /** 회차와 스냅샷 단계로 이미지 상태를 집계하며 미기록과 명시적인 이미지 없음을 구분한다. */
  function Images(run, phase = 'Final') {
    const snapshot = phase === 'Initial' || run.complete ? run.snapshots.get(phase) : null;
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
    return {
      caseRetained, keycapRetained: withImage.length > 0 ? true : capCount === null ? null : false, keycaps: capCount, canvas: withImage.filter(row => row.element_id.startsWith("Canvas:")).length,
      individual: withImage.filter(row => row.element_id.startsWith("Individual:")).length,
      retained: caseRetained === true || withImage.length > 0 ? true : caseRetained === false && capCount !== null ? false : null
    };
  }

  /** 회차와 이미지 범위의 적용·교체·제거를 집계하며 성공 동작 전후 상태로 값 변경 횟수를 반환한다. */
  function ImageSelections(run, scope) {
    const matches = part => scope === '케이스' ? part === 'Image:CaseWhole' : part?.startsWith('Image:Keycap:');
    const actions = run.rows.filter(row => row.record_type === 'ImageAction' && ['Apply', 'Replace', 'Remove'].includes(row.element_id)
      && (scope === '케이스' ? row.target_scope === 'CaseWhole' : ['SingleKeycap', 'AllKeycapsCanvas'].includes(row.target_scope)));
    let changed = 0, known = true;
    actions.forEach(row => {
      if (row.apply_success === false) return;
      if (row.apply_success !== true) { known = false; return; }
      const before = run.snapshotBatches.findLast(batch => batch.rows.at(-1).sequence < row.sequence);
      const after = run.snapshotBatches.find(batch => batch.preceding === row);
      if (!before?.reliable || !after?.reliable) { known = false; return; }
      const continuous = run.rows.filter(item => item.sequence >= before.rows.at(-1).sequence && item.sequence <= row.sequence);
      if (!continuous.length || continuous.some((item, i, items) => !Number.isFinite(item.sequence) || i && item.sequence !== items[i - 1].sequence + 1)) { known = false; return; }
      const keys = row.target_scope === 'SingleKeycap' && Number.isInteger(row.keycap_index) && row.keycap_index >= 0
        ? ['Image:Keycap:' + row.keycap_index] : [...new Set([...before.values.keys(), ...after.values.keys()].filter(matches))];
      if (row.target_scope === 'SingleKeycap' && (!Number.isInteger(row.keycap_index) || row.keycap_index < 0)) { known = false; return; }
      const pairs = keys.map(part => [before.values.get(part)?.element_id, after.values.get(part)?.element_id]);
      if (pairs.some(([a, b]) => a != null && b != null && a !== b)) changed++;
      else if (!pairs.length || pairs.some(([a, b]) => a == null || b == null)) known = false;
    });
    return { attempts: actions.length, success: actions.every(row => typeof row.apply_success === 'boolean') ? actions.filter(row => row.apply_success).length : null, changed: known ? changed : null };
  }

  /** 성공한 복원 직전·직후의 연속 스냅샷을 비교해 실제 값이 달라진 파츠 식별자 집합을 반환한다. */
  function LoadChanges(run) {
    const changed = new Set();
    run.rows.filter(row => row.record_type === 'Load' && row.apply_success === true).forEach(row => {
      const previous = run.snapshotBatches.findLast(batch => batch.rows.at(-1).sequence < row.sequence);
      const restored = run.snapshotBatches.find(batch => batch.preceding === row && batch.reason === 'Load:' + (row.element_id ?? ''));
      if (!previous?.reliable || !restored?.reliable) return;
      const between = run.rows.filter(item => item.sequence >= previous.rows.at(-1).sequence && item.sequence <= row.sequence);
      if (!between.length || between.some((item, i, rows) => !Number.isFinite(item.sequence) || i && item.sequence !== rows[i - 1].sequence + 1)) return;
      restored.values.forEach((value, part) => {
        const before = previous.values.get(part)?.element_id, after = value.element_id;
        if (part.startsWith('Keycap:')) {
          const beforeKey = previous.values.get(part);
          if (beforeKey && ['is_installed', 'element_id', 'keycap_kind', 'material_id'].some(field => beforeKey[field] != null && value[field] != null && beforeKey[field] !== value[field])) changed.add(part);
        } else if (before != null && after != null && before !== after) changed.add(part);
      });
    });
    return changed;
  }

  /** 회차의 이미지 식별자·배치 스냅샷과 성공 동작을 연결해 키캡·케이스의 결과 상태를 반환한다. */
  function ImageResults(run) {
    const initial = run.snapshots.get('Initial'), final = run.complete ? run.snapshots.get('Final') : null;
    const beforeUsage = Images(run, 'Initial'), afterUsage = Images(run);
    const loadChanges = LoadChanges(run);
    return [['키캡', 'keycapRetained'], ['케이스', 'caseRetained']].map(([scope, field]) => {
      const matches = part => scope === '케이스' ? part === 'Image:CaseWhole' : part?.startsWith('Image:Keycap:');
      const keys = new Set(run.snapshotBatches.flatMap(batch => [...batch.values.keys()].filter(matches)));
      if (scope === '케이스') keys.add('Image:CaseWhole');
      const complete = snapshot => keys.size > 0 && [...keys].every(part => snapshot?.get(part)?.element_id != null);
      const equal = snapshot => [...keys].every(part => snapshot?.get(part)?.element_id === initial?.get(part)?.element_id);
      const before = beforeUsage[field], after = afterUsage[field];
      const loadChanged = [...keys].some(part => loadChanges.has(part));
      const result = { scope, before, after, loadChanged, ...ImageSelections(run, scope) };
      if (!complete(initial) || !complete(final) || before === null || after === null)
        return { ...result, status: '판정 불가', reason: '초기·최종 이미지 대상의 상태가 미기록이거나 일부만 확인됩니다.' };
      if (!equal(final)) return { ...result, status: '변경 유지', reason: '' };
      const actions = run.rows.filter(row => row.record_type === 'Load' || row.record_type === 'ImageAction' && ['Apply', 'Replace', 'Remove'].includes(row.element_id)
        && (scope === '케이스' ? row.target_scope === 'CaseWhole' : ['SingleKeycap', 'AllKeycapsCanvas'].includes(row.target_scope)));
      const continuous = !!run.start && !!run.end && run.rows.every((row, i, rows) => Number.isFinite(row.sequence) && (!i || row.sequence === rows[i - 1].sequence + 1));
      const tracked = actions.every(row => row.apply_success === false || row.apply_success === true && run.snapshotBatches.some(batch => batch.preceding === row && batch.reliable && complete(batch.values)));
      const unknownScope = run.rows.some(row => row.record_type === 'ImageAction' && ['Apply', 'Replace', 'Remove'].includes(row.element_id) && row.apply_success !== false && !['CaseWhole', 'SingleKeycap', 'AllKeycapsCanvas'].includes(row.target_scope));
      if (!continuous || !tracked || unknownScope)
        return { ...result, status: '판정 불가', reason: '이미지 변경·복원 기록이나 직후 스냅샷이 불완전하여 초기 유지와 원복을 구분할 수 없습니다.' };
      const away = run.snapshotBatches.some(batch => batch.reliable && complete(batch.values) && !equal(batch.values));
      return { ...result, status: away ? '원복' : '초기 유지', reason: '' };
    });
  }

  /** 선택 범위 회차의 여섯 항목별 클릭·방문·체류·접근률을 계산해 반환한다. */
  function Parts(runs) {
    return Object.keys(PARTS).map(part => {
      const events = runs.flatMap(run => run.rows.filter(row => SameTabPart(row.part, part)));
      const visits = runs.flatMap(run => run.visits.filter(visit => visit.part === part));
      const ended = visits.filter(visit => visit.end);
      const offered = runs.filter(run => run.partOffered.has(part));
      const accessed = offered.filter(run => run.visits.some(visit => visit.part === part));
      const perRun = runs.map(run => {
        const items = run.visits.filter(visit => visit.part === part);
        if (!items.length && !run.partOffered.has(part)) return null;
        return items.some(visit => !visit.end) ? null : Sum(items.map(visit => visit.focused));
      });
      return {
        part, open: events.filter(row => row.record_type === "PartAccess" && row.element_id === "Open").length,
        close: events.filter(row => row.record_type === "PartAccess" && row.element_id === "Close").length,
        visits: visits.filter(visit => visit.start).length, unclosed: visits.filter(visit => !visit.end).length,
        duration: visits.length && !ended.length ? null : Sum(ended.map(visit => visit.duration)), focused: visits.length && !ended.length ? null : Sum(ended.map(visit => visit.focused)),
        offered: offered.length, accessed: accessed.length, rate: offered.length ? accessed.length / offered.length : null,
        revisits: runs.reduce((sum, run) => sum + Math.max(0, run.visits.filter(visit => visit.part === part && visit.start).length - 1), 0),
        selected: runs.reduce((sum, run) => sum + run.selected.filter(row => SameTabPart(row.part, part)).length, 0), distribution: Describe(perRun)
      };
    });
  }

  /** 제공 요소와 선택을 빌드·항목별로 연결하고 선택 비중과 최종 유지 분모를 반환한다. */
  function Elements(runs, part) {
    if (part === 'Keycap') runs = runs.filter(run => run.version < 2);
    const all = runs.flatMap(run => run.rows.filter(row => SameElementPart(row.part, part) && ["AvailableOption", "ElementSelected"].includes(row.record_type)));
    return [...Playtest.Group(all, row => JSON.stringify([row.build_version, row.element_id])).values()].map(rows => {
      const element = rows[0].element_id, build = rows[0].build_version;
      const buildRuns = runs.filter(run => run.build === build);
      const selected = rows.filter(row => row.record_type === "ElementSelected");
      const offered = buildRuns.filter(run => run.options.some(row => SameElementPart(row.part, part) && row.element_id === element));
      const eligible = offered.filter(run => run.complete && run.snapshots.get("Final")?.get(part)?.element_id !== null && run.snapshots.get("Final")?.has(part));
      const retained = eligible.filter(run => run.snapshots.get("Final").get(part).element_id === element);
      const denominator = all.filter(row => row.record_type === "ElementSelected" && row.build_version === build).length;
      const testers = new Set(buildRuns.flatMap(run => run.selected.filter(row => SameElementPart(row.part, part) && row.element_id === element).map(() => run.tester)).filter(Boolean));
      const offeredTesters = new Set(offered.map(run => run.tester).filter(Boolean));
      return {
        element, build, ...Selections(selected),
        share: denominator ? selected.length / denominator : null, denominator,
        offered: offered.length, eligible: eligible.length, retained: retained.length, finalRate: eligible.length ? retained.length / eligible.length : null,
        selectedTesters: testers.size, offeredTesters: offeredTesters.size
      };
    });
  }

  /** 이미지 기능 제공·성공 적용 회차와 완료 후 유지 상태를 분모와 함께 반환한다. */
  function ImageMetrics(runs) {
    const offered = runs.filter(run => run.partOffered.has("CustomImage"));
    const used = offered.filter(run => run.imageApplies.length);
    const completedUsed = runs.filter(run => run.complete && run.imageApplies.length);
    const known = completedUsed.filter(run => Images(run).retained !== null);
    return {
      offered: offered.length, used: used.length, usage: offered.length ? used.length / offered.length : null,
      completedUsed: completedUsed.length, known: known.length, retained: known.filter(run => Images(run).retained).length,
      retention: completedUsed.length && known.length === completedUsed.length ? known.filter(run => Images(run).retained).length / completedUsed.length : null
    };
  }

  const FINAL_PARTS = ['Keycap', 'Switch', 'Case', 'Plate', 'PlateEffect', 'LED', 'LEDColor', 'LEDEnabled'];
  /** 회차의 순서 연속성과 성공 선택의 이전값 연결을 확인하여 파츠별 초기·최종 판정을 반환한다. */
  function FinalParts(run) {
    const loadChanges = LoadChanges(run);
    const initial = run.snapshots.get('Initial'), final = run.complete ? run.snapshots.get('Final') : null;
    const continuous = !!run.start && !!run.end && run.rows.every((row, i, rows) => row.status.apply_success !== 'missing' && row.status.value_changed !== 'missing' && Number.isFinite(row.sequence) && (!i || row.sequence === rows[i - 1].sequence + 1));
    return FINAL_PARTS.map(part => {
      if (part === 'Keycap' && run.version >= 2) return KeycapResult(run);
      const before = initial?.get(part)?.element_id ?? null, after = final?.get(part)?.element_id ?? null;
      const reasons = [];
      if (before === null) reasons.push('초기 상태가 미기록이거나 스냅샷 묶음을 확정할 수 없습니다.');
      if (after === null) reasons.push('최종 상태가 미기록이거나 스냅샷 묶음을 확정할 수 없습니다.');
      if (!run.start || !run.end) reasons.push('편집 시작 또는 종료 기록이 없습니다.');
      if (run.rows.some(row => row.status.apply_success === 'missing' || row.status.value_changed === 'missing')) reasons.push('선택 성공·값 변경 여부를 확인할 열이 없습니다.');
      if (run.rows.some((row, i, rows) => !Number.isFinite(row.sequence) || (i && row.sequence !== rows[i - 1].sequence + 1))) reasons.push('이벤트 순번(sequence)이 누락되거나 연속적이지 않습니다.');
      const actions = run.rows.filter(row => part === 'LEDColor' || part === 'LEDEnabled'
        ? row.record_type === 'Adjustment' && row.part === 'LED' && row.element_id === (part === 'LEDColor' ? 'Color' : 'Power')
        : row.record_type === 'ElementSelected' && SameElementPart(row.part, part));
      let current = before, away = false, reliable = continuous && before !== null;
      actions.forEach(row => {
        if (row.apply_success === false) return;
        const value = part === 'LEDColor' || part === 'LEDEnabled' ? row.details : row.element_id;
        if (row.apply_success !== true || value == null || typeof row.value_changed !== 'boolean') { reasons.push('선택·조절의 성공 여부, 적용값 또는 변경 여부가 미기록입니다.'); reliable = false; return; }
        const previousValue = part === 'LEDEnabled' && row.previous_element_id == null && ['true', 'false'].includes(current) && value === (current === 'true' ? 'false' : 'true') ? current : row.previous_element_id;
        if (previousValue !== current || row.value_changed !== (current !== value)) { reasons.push('이전값과 다음 선택값의 연결 또는 변경 표시가 일치하지 않습니다.'); reliable = false; }
        if (row.value_changed && value !== before) away = true;
        current = value;
      });
      if (run.rows.some(row => row.record_type === 'Load' && row.apply_success !== false)) { reasons.push('복원(Load) 기록이 있어 선택 기록만으로 변경 경로를 확정할 수 없습니다.'); reliable = false; }
      if (current !== after) { reasons.push('선택·조절 기록으로 추적한 마지막 값이 Final 상태와 다릅니다.'); reliable = false; }
      const counts = (part === 'LEDColor' || part === 'LEDEnabled') ? {
        attempts: actions.length, success: actions.every(row => typeof row.apply_success === 'boolean') ? actions.filter(row => row.apply_success).length : null,
        changed: actions.every(row => row.apply_success === false || typeof row.value_changed === 'boolean') ? actions.filter(row => row.apply_success === true && row.value_changed).length : null,
        reselected: actions.every(row => row.apply_success === false || typeof row.value_changed === 'boolean') ? actions.filter(row => row.apply_success === true && row.value_changed === false).length : null
      } : Selections(actions);
      return { part, before, after, loadChanged: loadChanges.has(part), ...counts, reason: [...new Set(reasons)].join(' '), status: before === null || after === null ? '판정 불가' : before !== after ? '변경 유지' : !reliable ? '판정 불가' : away ? '원복' : '초기 유지' };
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
    const keys = new Set([...FINAL_PARTS.filter(part => run.version < 2 || part !== 'Keycap'), ...[...(saved?.keys() || []), ...(final?.keys() || [])].filter(part => part.startsWith('Image:') || run.version >= 2 && part.startsWith('Keycap:')), 'Image:CaseWhole']);
    if (run.version >= 2 && ![...keys].some(part => part.startsWith('Keycap:'))) keys.add('Keycap:미기록');
    if (![...keys].some(part => part.startsWith('Image:Keycap:'))) keys.add('Image:Keycap:미기록');
    const different = [], unknown = [];
    keys.forEach(part => {
      const a = saved?.get(part), b = final?.get(part);
      if (run.version >= 2 && part.startsWith('Keycap:')) {
        if (!KeyKnown(a) || !KeyKnown(b)) unknown.push(part); else if (KeySignature(a) !== KeySignature(b)) different.push(part);
      } else if (a?.element_id == null || b?.element_id == null) unknown.push(part); else if (a.element_id !== b.element_id) different.push(part);
    });
    return {
      save: stats('Save'), load: stats('Load'), last, different, unknown,
      status: !last ? stats('Save').attempts ? '성공 저장 기록 없음' : '저장 기록 없음' : !saved || !final ? '저장 성공, 상태 비교 불가' : different.length ? '저장 후 추가 변경 있음' : unknown.length ? '저장 성공, 상태 비교 불가' : '저장 후 변경 없음'
    };
  }
  /** 완료 회차의 빌드별 탐색과 최종 구성을 집계하고 각 비율의 독립 분모를 반환한다. */
  function FinalElements(runs) {
    const completed = runs.filter(run => run.complete), result = [];
    for (const part of FINAL_PARTS.slice(0, 6)) {
      for (const [build, items] of Playtest.Group(completed.filter(run => part !== 'Keycap' || run.version < 2), run => run.build)) {
        const known = items.filter(run => run.snapshots.get('Final')?.get(part)?.element_id != null);
        const values = new Set(items.flatMap(run => [...run.options.filter(row => SameElementPart(row.part, part)).map(row => row.element_id), ...run.selected.filter(row => SameElementPart(row.part, part)).map(row => row.element_id), run.snapshots.get('Final')?.get(part)?.element_id]).filter(value => value != null));
        values.forEach(element => {
          const matching = run => run.selected.filter(row => SameElementPart(row.part, part) && row.element_id === element);
          const selected = items.filter(run => matching(run).length), successful = items.filter(run => matching(run).some(row => row.apply_success === true));
          const selectedKnown = successful.filter(run => known.includes(run)), retained = known.filter(run => run.snapshots.get('Final').get(part).element_id === element);
          result.push({
            part, build, element, ...Selections(selected.flatMap(matching)), testers: new Set(selected.map(run => run.tester).filter(Boolean)).size, selected: selected.length,
            denominator: known.length, retained: retained.length, selectedKnown: selectedKnown.length, direct: retained.filter(run => successful.includes(run)).length,
            initial: retained.filter(run => FinalParts(run).find(item => item.part === part).status === '초기 유지').length,
            finalChanged: retained.filter(run => { const before = run.snapshots.get('Initial')?.get(part)?.element_id; return before != null && before !== element; }).length
          });
        });
      }
    }
    return result;
  }
  /** 완료 회차의 완전한 기본 조합과 확인 가능한 이미지 유형을 빌드별로 집계한다. */
  function Combinations(runs) {
    const completed = runs.filter(run => run.complete), basic = [], image = [];
    const baseParts = ['Keycap', 'Switch', 'Case', 'Plate', 'LED'];
    let excluded = 0;
    completed.forEach(run => {
      const values = baseParts.map(part => part === 'Keycap' && run.version >= 2 ? KeycapComposition(run, 'Final', true) : run.snapshots.get('Final')?.get(part)?.element_id);
      const displayValues = values.slice();
      if (run.version >= 2 && values[0] != null) displayValues[0] = 'v2+ 개별 키: ' + JSON.stringify(JSON.parse(values[0]).map(tuple => tuple.map((value, index) => index === 2 ? KeycapName(value) : value)));
      else if (values[0] != null) displayValues[0] = 'v1 공통 재질: ' + values[0];
      if (values[0] != null) values[0] = (run.version >= 2 ? 'v2+ 개별 키: ' : 'v1 공통 재질: ') + values[0];
      if (values.some(value => value == null)) excluded++; else basic.push({ run, value: values.join(' + '), display: displayValues.join(' + '), build: run.build });
      const state = Images(run);
      const type = state.caseRetained === null || state.keycaps === null ? '판정 불가' : state.caseRetained ? state.keycaps > 0 ? '케이스와 키캡' : '케이스만' : state.keycaps > 0 ? '키캡만' : '없음';
      image.push({ run, value: type, build: run.build });
    });
    const group = rows => [...Playtest.Group(rows, row => JSON.stringify([row.build, row.value])).values()].map(items => ({ value: items[0].value, display: items[0].display ?? items[0].value, build: items[0].build, count: items.length, testers: new Set(items.map(item => item.run.tester).filter(Boolean)).size, denominator: rows.filter(row => row.build === items[0].build).length })).sort((a, b) => b.count - a.count);
    return { basic: group(basic), image: group(image), excluded };
  }

  /** 개요 범위의 종류별 키캡 선택·적용과 정상 종료 장착 수 및 미수집 회차를 분리해 반환한다. */
  function KeycapOverview(runs) {
    const modern = runs.filter(run => run.version >= 2), completed = modern.filter(run => run.complete);
    const states = completed.map(run => KeyStates(run));
    const known = states.filter(items => [...items.values()].some(row => typeof row.is_installed === 'boolean'));
    const installed = known.flatMap(items => [...items.values()].filter(row => row.is_installed === true));
    const actions = modern.flatMap(run => run.rows.filter(row => row.schema_version >= 2 && row.part === 'Keycap'));
    const kinds = ['Regular', 'Artisan', null].map(kind => {
      const picked = actions.filter(row => row.record_type === 'ElementPicked' && (row.keycap_kind ?? null) === kind);
      const applies = actions.filter(row => row.record_type === 'KeycapApply' && (row.keycap_kind ?? null) === kind);
      return { kind, picked: modern.length ? picked.length : null, attempts: modern.length ? applies.length : null,
        success: modern.length && applies.every(row => typeof row.apply_success === 'boolean') ? applies.filter(row => row.apply_success).length : null,
        installed: known.length ? installed.filter(row => (row.keycap_kind ?? null) === kind).length : null };
    });
    return { kinds, modern: modern.length, legacy: runs.length - modern.length, finalKnown: known.length, finalMissing: completed.length - known.length,
      unknownInstalled: states.reduce((sum, items) => sum + [...items.values()].filter(row => typeof row.is_installed !== 'boolean').length, 0) };
  }

  /** 재질 기반 키캡 ID의 표시 이름만 디코딩하며 집계용 원본 ID와 파츠 ID는 변경하지 않는다. */
  function KeycapName(id) {
    if (typeof id !== 'string' || !id.startsWith('material:')) return id;
    const name = id.slice('material:'.length);
    try { return decodeURIComponent(name); } catch { return name; }
  }

  /** 개별 키 스냅샷의 part와 숫자 인덱스를 대조하여 실제 키 인덱스 또는 null을 반환한다. */
  function KeyIndex(row) {
    const match = /^Keycap:(\d+)$/.exec(row.part || '');
    const fromPart = match ? Number(match[1]) : null;
    const fromColumn = Number.isInteger(row.keycap_index) && row.keycap_index >= 0 ? row.keycap_index : null;
    if (fromPart !== null && fromColumn !== null && fromPart !== fromColumn) return null;
    return fromPart ?? ((row.part === 'Keycap' || row.part?.startsWith('Keycap:')) ? fromColumn : null);
  }
  /** v2 회차의 지정 스냅샷에서 개별 키를 인덱스로 연결하고 Final의 중단 대체를 금지한다. */
  function KeyStates(run, phase = 'Final') {
    const snapshot = run.version >= 2 && (phase !== 'Final' || run.complete) ? run.snapshots.get(phase) : null;
    return new Map([...(snapshot?.entries() || [])].filter(([part]) => /^Keycap:\d+$/.test(part)).map(([part, row]) => [Number(part.slice(7)), row]));
  }
  /** 개별 키의 장착 여부와 장착 파츠의 필수 정의가 확인되는지 반환한다. */
  function KeyKnown(row) { return typeof row?.is_installed === 'boolean' && (row.is_installed === false || row.element_id != null && row.keycap_kind != null && row.material_id != null); }
  /** 개별 키의 원본 파츠·종류·재질·장착 여부를 비교용 문자열로 반환한다. */
  function KeySignature(row) { return JSON.stringify([row?.element_id ?? null, row?.keycap_kind ?? null, row?.material_id ?? null, row?.is_installed ?? null]); }
  /** 모든 스냅샷에서 관찰된 키 범위에 비추어 지정 시점의 완전한 개별 키 구성을 반환한다. */
  function KeycapComposition(run, phase = 'Final', strict = false) {
    const states = KeyStates(run, phase);
    const expected = new Set(run.snapshotBatches.flatMap(batch => [...batch.values.keys()].filter(part => /^Keycap:\d+$/.test(part))));
    if (!states.size || states.size !== expected.size || [...states.values()].some(row => typeof row.is_installed !== 'boolean')) return null;
    const rows = [...states].sort((a, b) => a[0] - b[0]);
    if (strict) return rows.some(([, row]) => !KeyKnown(row)) ? null : JSON.stringify(rows.map(([index, row]) => [index, row.is_installed, ...(row.is_installed ? [row.element_id, row.keycap_kind, row.material_id] : [])]));
    const installed = rows.filter(([, row]) => row.is_installed);
    return installed.length + '개 장착 / Regular ' + installed.filter(([, row]) => row.keycap_kind === 'Regular').length + ' / Artisan ' + installed.filter(([, row]) => row.keycap_kind === 'Artisan').length + ' / 종류 미기록 ' + installed.filter(([, row]) => row.keycap_kind == null).length + ' · 관찰 ' + states.size + '키';
  }
  /** v2의 키캡 최종 결과를 공통 재질 대신 개별 키 구성과 적용 조작으로 판정한다. */
  function KeycapResult(run) {
    const before = KeycapComposition(run, 'Initial'), after = KeycapComposition(run);
    const a = KeycapComposition(run, 'Initial', true), b = KeycapComposition(run, 'Final', true);
    const applies = run.selected.filter(row => row.record_type === 'KeycapApply');
    const continuous = !!run.start && !!run.end && run.rows.every((row, i, rows) => Number.isFinite(row.sequence) && (!i || row.sequence === rows[i - 1].sequence + 1));
    const initialKeys = KeyStates(run, 'Initial');
    const finalKeys = KeyStates(run);
    const confirmedChange = [...initialKeys].some(([index, row]) => {
      const final = finalKeys.get(index);
      return final && ['is_installed', 'element_id', 'keycap_kind', 'material_id'].some(field => row[field] != null && final[field] != null && row[field] !== final[field]);
    });
    const changedAway = run.snapshotBatches.some(batch => [...initialKeys].some(([index, row]) => {
      const current = batch.values.get('Keycap:' + index); return batch.reliable && KeyKnown(current) && KeySignature(current) !== KeySignature(row);
    }));
    const tracked = run.rows.filter(row => row.record_type === 'KeycapApply' || row.record_type === 'Load').every(row => row.apply_success === false || row.apply_success === true && run.snapshotBatches.some(batch => batch.preceding === row && batch.reliable && [...initialKeys.keys()].every(index => KeyKnown(batch.values.get('Keycap:' + index)))));
    const status = confirmedChange ? '변경 유지' : a === null || b === null ? '판정 불가' : a !== b ? '변경 유지' : !continuous || !tracked ? '판정 불가' : changedAway ? '원복' : '초기 유지';
    return { part: 'Keycap', before, after, ...Selections(applies), status, reason: status === '판정 불가' ? '개별 키 스냅샷·정의·장착 여부 또는 적용/복원 연결이 부족합니다. 공통 재질 요약으로 대신 판정하지 않습니다.' : '', loadChanged: [...LoadChanges(run)].some(part => part.startsWith('Keycap:')), finalDifferent: confirmedChange ? true : a !== null && b !== null ? a !== b : null };
  }
  /** v2의 종류·파츠 ID·빌드별 버튼 선택, 적용 조작, 완료 Final 장착과 고유 플레이어를 따로 집계한다. */
  function KeycapStats(runs) {
    const modern = runs.filter(run => run.version >= 2);
    const rows = modern.flatMap(run => [
      ...run.rows.filter(row => row.schema_version >= 2 && row.part === 'Keycap' && ['AvailableOption', 'ElementPicked', 'KeycapApply'].includes(row.record_type)).map(row => ({ run, row, final: false })),
      ...[...KeyStates(run).values()].filter(row => row.is_installed === true).map(row => ({ run, row, final: true }))
    ]);
    return [...Playtest.Group(rows, item => JSON.stringify([item.run.build, item.row.keycap_kind ?? null, item.row.element_id ?? null])).values()].map(items => {
      const { run, row } = items[0], picked = items.filter(item => !item.final && item.row.record_type === 'ElementPicked'), applies = items.filter(item => !item.final && item.row.record_type === 'KeycapApply');
      const final = items.filter(item => item.final), known = modern.filter(item => item.build === run.build && item.complete && [...KeyStates(item).values()].some(value => typeof value.is_installed === 'boolean'));
      return { build: run.build, kind: row.keycap_kind ?? null, element: row.element_id ?? null, picked: picked.length, attempts: applies.length,
        success: applies.every(item => typeof item.row.apply_success === 'boolean') ? applies.filter(item => item.row.apply_success).length : null,
        failed: applies.every(item => typeof item.row.apply_success === 'boolean') ? applies.filter(item => item.row.apply_success === false).length : null,
        players: new Set(picked.map(item => item.run.fileId)).size, runs: new Set(picked.map(item => item.run.key)).size,
        installed: known.length ? final.length : null, installedPlayers: known.length ? new Set(final.map(item => item.run.fileId)).size : null,
        installedRuns: known.length ? new Set(final.map(item => item.run.key)).size : null, finalKnown: known.length };
    });
  }

  return { PARTS, ELEMENT_PARTS, COLORS, SameElementPart, SameTabPart, Decode, Sum, Describe, Build, Images, ImageResults, Parts, Elements, IsImageApply, ImageMetrics, Selections, FINAL_PARTS, FinalParts, ImageChanges, Storage, FinalElements, Combinations, KeycapOverview, KeycapName, KeyIndex, KeyStates, KeyKnown, KeySignature, KeycapComposition, KeycapStats };
})();
