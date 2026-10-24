"use strict";

const CustomUI = (() => {
  const c = { files: [], file: "ALL", tester: "ALL", session: "ALL", run: "ALL", part: "Keycap", keycapKind: 'ALL', element: "", tab: "summary", completedOnly: false, excludeEmpty: false };
  const titles = { summary: ["커스터마이징 개요", "접근·체류·탐색을 살펴봅니다. 행동 기록만으로 재미나 만족을 단정하지 않습니다."], runs: ["편집 흐름 · 최종 구성", "우클릭 진입부터 종료까지, 방문 구간과 최종 유지 상태를 구분합니다."], elements: ["항목 · 요소 선택", "많이 눌러본 요소와 마지막에 유지한 요소는 다른 지표입니다."], images: ["이미지 사용", "미리보기 읽기와 실제 대상 적용, 최종 유지를 구분합니다."], compare: ["플레이어 · 회차 비교", "회차 수와 사람 수를 분리하고, 같은 플레이어의 반복 편집을 비교합니다."] };

  titles.results = ['최종 결과물 분석', '탐색·최종 구성·이미지·저장 상태를 구분합니다. Final은 저장 완료를 뜻하지 않습니다.'];

  /** 선택한 파일·플레이어·세션·회차와 명시된 제외 조건에 맞는 회차를 반환한다. */
  function Runs(ignoreRun = false) {
    const selected = c.files.filter(file => c.file === "ALL" || file.id === c.file).flatMap(file => file.runs)
      .filter(run => (c.tester === "ALL" || (run.tester || "미기록") === c.tester) &&
        (c.session === "ALL" || (run.session || "미기록") === c.session) && (ignoreRun || c.run === "ALL" || run.key === c.run) &&
        (!c.completedOnly || run.complete) && (!c.excludeEmpty || run.selected.length > 0 || run.picked.length > 0 || run.imageApplies.length > 0));
    return [...new Map(selected.map(run => [JSON.stringify([run.fileId, run.session, run.id]), run])).values()];
  }
  /** 미기록과 수집된 숫자를 구분하여 커스터마이징 화면용 문자열을 반환한다. */
  function V(value, digits = 3) { return value === null || value === undefined ? "미기록" : typeof value === 'boolean' ? value ? '예' : '아니오' : F(value, typeof value === 'number' && value !== 0 && Math.abs(value) < .0005 ? 6 : digits); }
  /** 키캡 표시 이름을 이스케이프하고 재질 기반 ID는 툴팁에 원본을 보존한다. */
  function KeycapValue(id) {
    const label = V(Customization.KeycapName(id));
    return typeof id === 'string' && id.startsWith('material:') ? '<span title="원본 ID: ' + E(id) + '">' + label + '</span>' : label;
  }
  /** 원본값 상태를 사용하여 잘못된 값과 미기록을 구분한다. */
  function Value(row, field, digits = 3) { return row?.status[field] === "invalid" ? "잘못된 값: " + E(row.raw[field]) : V(row?.[field], digits); }
  /** 파일 식별자를 받아 불러온 순서에 따른 플레이어 표시 이름을 반환한다. */
  function PlayerLabel(id) { return '플레이어 ' + (c.files.findIndex(file => file.id === id) + 1); }
  /** 플레이어와 회차 번호로 긴 원본 ID를 대체할 짧은 표시 이름을 반환한다. */
  function Label(run) {
    const sessions = [...new Set(c.files.flatMap(file => file.runs.map(item => item.session)))];
    return PlayerLabel(run.fileId) + (sessions.length > 1 ? " · 세션 " + (sessions.indexOf(run.session) + 1) : "") + " · " + run.index + "회차";
  }
  /** 분자와 분모를 함께 표시한 비율 문자열을 반환한다. */
  function Rate(numerator, denominator) { return denominator ? F(numerator / denominator * 100, 1) + "% (" + numerator + "/" + denominator + ")" : "계산 불가 (분모 0)"; }
  /** 항목별 색상과 여러 구간의 동일 눈금 막대를 수치 라벨과 함께 생성한다. */
  function Bars(items, unit) {
    const maximum = Math.max(1, ...items.map(item => item.segments.reduce((sum, segment) => sum + (Number.isFinite(segment.value) ? Math.max(0, segment.value) : 0), 0)));
    return '<div class="custom-chart-scroll">' + items.map(item => '<div class="custom-bar"><b style="border-color:' + item.color + '">' + E(item.label) + '</b><div><div class="custom-track">' + item.segments.map(segment => Number.isFinite(segment.value) ? '<span style="width:' + Math.max(0, segment.value) / maximum * 100 + '%;background:' + item.color + ';opacity:' + (segment.dim ? '.3' : '1') + '"></span>' : '').join('') + '</div><small>' + item.segments.map(segment => E(segment.label) + ' ' + V(segment.value) + E(unit)).join(' / ') + (item.note ? ' · ' + E(item.note) : '') + '</small></div></div>').join('') + '</div>';
  }
  /** 선택지 목록을 새로 만들고 현재 유효한 선택값을 유지한다. */
  function Fill(id, options, value) {
    const select = document.getElementById(id);
    select.replaceChildren(...options.map(option => new Option(option[1], option[0])));
    select.value = options.some(option => option[0] === value) ? value : "ALL";
    select.disabled = options.length <= 1;
    return select.value;
  }
  /** 독립 커스터마이징 데이터의 상단 필터와 제거 버튼 상태를 동기화한다. */
  function Sync() {
    c.file = Fill("customFileSelect", [["ALL", "모든 커스터마이징 파일"], ...c.files.map(file => [file.id, file.name])], c.file);
    const fileRuns = c.files.filter(file => c.file === "ALL" || file.id === c.file).flatMap(file => file.runs);
    c.tester = Fill("testerSelect", [["ALL", "모든 플레이어"], ...c.files.filter(file => c.file === 'ALL' || file.id === c.file).map(file => [file.id, PlayerLabel(file.id) + ' · ' + file.name])], c.tester);
    const testerRuns = fileRuns.filter(run => c.tester === "ALL" || (run.tester || "미기록") === c.tester);
    c.session = Fill("customSessionSelect", [["ALL", "모든 세션"], ...[...new Set(testerRuns.map(run => run.session))].map((session, index) => [session || "미기록", "세션 " + (index + 1) + " · " + (session?.slice(0, 8) || "미기록")])], c.session);
    c.run = Fill("customRunSelect", [["ALL", "모든 편집 회차"], ...Runs(true).map(run => [run.key, Label(run) + " · " + (run.complete ? "정상 종료" : "미완료")])], c.run);
    document.getElementById("removeFile").disabled = !c.files.length || (c.file === "ALL" && c.files.length !== 1);
  }

  /** 회차별 완료 여부와 최종 키캡·이미지 구성을 요약하고 해당 회차의 최종 분석으로 연결한다. */
  function OverviewResults(runs) {
    return Panel('회차별 완성 결과', Table(['편집 회차', '종료 상태', '일반 장착(키)', '아티산 장착(키)', '종류 미기록 장착(키)', '이미지 (키캡 / 케이스)', '최종 분석'], runs.map(run => {
      const overview = Customization.KeycapOverview([run]), images = Customization.Images(run);
      const count = kind => !run.complete ? '집계 제외' : run.version < 2 ? '미수집' : V(overview.kinds.find(item => item.kind === kind).installed, 0);
      const image = value => !run.complete ? '집계 제외' : value === null ? '미기록' : String(value);
      return [E(Label(run)), run.complete ? '정상 종료' : '미완료 · ' + Value(run.end, 'end_reason'), count('Regular'), count('Artisan'), count(null), image(images.keycapRetained) + ' / ' + image(images.caseRetained), '<button data-custom-run="' + E(run.key) + '" data-custom-target="results">최종 결과 보기</button>'];
    }), 'customOverviewResults'), '완료 회차의 Final만 사용합니다. v1에는 일반/아티산 장착 정보가 미수집이며, 빈 종류와 누락 상태는 0으로 채우지 않습니다. 동일 키보드를 재편집해도 별도 회차로 표시합니다.');
  }

  /** 항목별 클릭·방문·체류와 전체 회차 지표를 명시적인 분모로 표시한다. */
  function Summary(runs) {
    const rows = runs.flatMap(run => run.rows), parts = Customization.Parts(runs);
    const selections = runs.flatMap(run => run.selected);
    const testers = new Set(runs.map(run => run.tester).filter(Boolean));
    const keycaps = Customization.KeycapOverview(runs);
    const keyValue = value => keycaps.modern ? V(value, 0) : '미수집';
    const total = field => parts.reduce((sum, part) => sum + part[field], 0);
    let html = '<h2>커스터마이징 활동 요약</h2><div class="metrics custom-overview-metrics">'
      + Metric('플레이어 / 편집 회차', testers.size + ' / ' + runs.length, '파일 1개 = 플레이어 1명')
      + Metric('정상 종료 / 미완료', runs.filter(run => run.complete).length + ' / ' + runs.filter(run => !run.complete).length, 'RightClickExit·NextScene과 is_complete=true')
      + Metric('전체 / 포커스 시간', V(Customization.Sum(runs.map(run => run.duration))) + ' / ' + V(Customization.Sum(runs.map(run => run.focused))), '초 · CustomizationEnd만 집계')
      + Metric('항목 열기 / 닫기', total('open') + ' / ' + total('close'), '회 · PartAccess, 실제 방문과 별도')
      + Metric('실제 방문 / 재방문', total('visits') + ' / ' + total('revisits'), '회 · PartVisitStart, 회차·항목별 두 번째부터 재방문')
      + Metric('요소 적용 시도 / 값 변경', selections.length + ' / ' + V(Customization.Selections(selections).changed, 0), '회 · v2 키캡 KeycapApply, 호환 이벤트 중복 제외') + '</div>';
    html += '<h2>키캡 탐색 · 장착 요약</h2><div class="metrics custom-overview-metrics">' + keycaps.kinds.filter(item => item.kind !== null).map(item => {
      const name = item.kind === 'Regular' ? '일반 (Regular)' : '아티산 (Artisan)';
      return Metric(name + ' · 버튼 선택', keyValue(item.picked), '회 · ElementPicked, 완료·중단 포함 현재 범위')
        + Metric(name + ' · 적용 시도 / 성공', keyValue(item.attempts) + ' / ' + keyValue(item.success), '회 · KeycapApply, 전체 적용도 조작 1회')
        + Metric(name + ' · 최종 장착', keyValue(item.installed), '키 · 정상 종료 Final의 is_installed=true만');
    }).join('') + '</div>';
    const unknown = keycaps.kinds.find(item => item.kind === null);
    html += '<p class="hint">종류별 지표: v2 ' + keycaps.modern + '회차 · v1 미수집 ' + keycaps.legacy + '회차 · 완료 Final 확인 ' + keycaps.finalKnown + '회차 / Final 미기록 ' + keycaps.finalMissing + '회차 · 장착 여부 미기록 ' + keycaps.unknownInstalled + '대상. 일반/아티산은 전투 효과 구분이 아닙니다.</p>';
    if (keycaps.modern && (unknown.picked || unknown.attempts || unknown.installed)) html += '<div class="warning">종류 미기록: 버튼 선택 ' + V(unknown.picked, 0) + '회 / 적용 시도 ' + V(unknown.attempts, 0) + '회 / 최종 장착 ' + V(unknown.installed, 0) + '키. 일반이나 아티산으로 추정하지 않습니다.</div>';
    if (keycaps.modern) {
      const chartKinds = keycaps.kinds.filter(item => item.kind !== null || item.picked || item.attempts || item.installed);
      const chartItems = (field, label) => chartKinds.map(item => ({ label: item.kind || '종류 미기록', color: item.kind === 'Artisan' ? '#ffad32' : item.kind === 'Regular' ? '#38a9ff' : '#687486', segments: [{ label, value: item[field] }] }));
      html += Panel('키캡 종류별 탐색 · 적용', Bars(chartItems('picked', '버튼 선택'), '회') + Bars(chartItems('attempts', '적용 시도'), '회'), '버튼 클릭과 적용 조작은 별도 지표입니다. 각 그래프는 두 종류에 같은 눈금을 사용하며 호환용 ElementSelected는 합산하지 않습니다.');
      html += Panel('키캡 종류별 최종 장착', Bars(chartItems('installed', 'Final 장착'), '키'), '정상 종료된 v2 회차의 관찰된 Final 장착 키 수 합계입니다. 같은 키보드를 다시 편집한 회차도 각각 포함하므로 고유 키보드의 키 개수와 다릅니다. 중단 회차와 v1은 제외합니다.');
    }
    html += OverviewResults(runs);
    html += Panel('항목 버튼 클릭 · 열기와 닫기', Bars(parts.map(part => ({ label: Customization.PARTS[part.part], color: Customization.COLORS[part.part], segments: [{ label: '열기', value: part.open }, { label: '닫기', value: part.close, dim: true }], note: '실제 방문 ' + part.visits + '회' })), '회'), 'PartAccess 클릭과 PartVisitStart 방문은 별개입니다. 닫기 클릭은 새 방문이 아닙니다.');
    html += Panel('항목별 체류 시간', Bars(parts.map(part => ({ label: Customization.PARTS[part.part], color: Customization.COLORS[part.part], segments: [{ label: '포커스', value: part.focused }, { label: '포커스 제외', value: part.duration === null || part.focused === null ? null : part.duration - part.focused, dim: true }], note: '미종료 방문 ' + part.unclosed + '회' })), '초') + '<p class="hint">종료된 방문의 포커스 체류 합계 ' + V(Customization.Sum(parts.map(part => part.focused)), 6) + '초. 회차 시간과 합산하지 않습니다. 미세한 음수 차이는 원본 수치로 표시하며 막대 폭만 0으로 제한합니다.</p>', 'PartVisitEnd만 합산합니다. 포커스 제외 구간의 정확한 위치는 미기록이므로 타임라인에 배치하지 않습니다.');
    html += Panel('항목 접근률 · 재방문 · 회차별 체류 분포', Table(['항목', '접근률(방문 회차/제공 회차)', '재방문', '요소 적용 시도', '평균 포커스', '중앙값', '알려진 회차 / 미기록'], parts.map(part => [E(Customization.PARTS[part.part]), Rate(part.accessed, part.offered), part.revisits, part.selected, V(part.distribution.mean) + '초', V(part.distribution.median) + '초', part.distribution.count + ' / ' + part.distribution.missing]), 'customParts'), '평균·중앙값은 현재 범위의 회차별 합계이며 무방문 0회차도 포함합니다. 미종료 방문은 0으로 채우지 않습니다.');
    html += Panel('회차 목록', Table(['편집 회차', '정상·중단', '전체 / 포커스(초)', '항목 접근 / 선택', '키보드 객체', '상세'], runs.map(run => [E(Label(run)), run.complete ? '정상 종료' : '미완료 · ' + Value(run.end, 'end_reason'), V(run.duration, 6) + ' / ' + V(run.focused, 6), run.rows.filter(row => row.record_type === 'PartAccess').length + ' / ' + run.selected.length, V(run.keyboard), '<button data-custom-run="' + E(run.key) + '">편집 상세</button>']), 'customRuns'), '아무 항목도 선택하지 않은 회차도 기본적으로 포함합니다. keyboard_id는 실행 중 객체이며 사람이나 다른 실행의 키보드를 식별하지 않습니다.');
    const files = c.files.filter(file => c.file === 'ALL' || file.id === c.file);
    html += Panel('파일 진단', files.map(file => '<details><summary>' + E(file.name) + ' · ' + file.rows.length + '행 / ' + file.header.length + '열</summary><p class="hint">' + (file.warnings.length ? file.warnings.map(E).join('<br>') : '파싱 경고 없음') + '</p></details>').join('') + '<details><summary>범위 내 원본 이벤트</summary>' + Table(['원본 레코드', 'sequence', '시각', '기록', '항목', '요소', 'details 원문'], rows.map(row => () => [row.sourceIndex, Value(row, 'sequence', 0), Value(row, 'elapsed_seconds', 6), V(row.record_type), E(Customization.ELEMENT_PARTS[row.part] || Customization.PARTS[row.part] || row.part || '해당 없음'), V(row.element_id), V(row.details)]), 'customRaw') + '</details>');
    return html;
  }

  /** 같은 시간 눈금에서 실제 방문 구간만 그려 회차별 편집 타임라인을 반환한다. */
  function Timeline(runs) {
    const known = runs.filter(run => Number.isFinite(run.start?.elapsed_seconds));
    if (!known.length) return Empty('편집 시작 시각 미기록');
    const max = Math.max(1, ...known.map(run => run.duration || 0), ...known.flatMap(run => run.visits.map(visit => Number.isFinite(visit.end?.end_elapsed_seconds) ? visit.end.end_elapsed_seconds - run.start.elapsed_seconds : 0)));
    const height = 55 + known.length * 62;
    let svg = '<svg style="--chart-width:930px" viewBox="0 0 930 ' + height + '" role="img" aria-label="편집 회차별 실제 항목 방문 시간"><text x="160" y="16">우클릭 편집 진입 후 시간 (초) · 모든 회차 공통 눈금</text>';
    const x = seconds => 160 + seconds / max * 735;
    for (let tick = 0; tick <= 5; tick++) svg += '<text x="' + x(max * tick / 5) + '" y="35" text-anchor="middle">' + F(max * tick / 5, 1) + '</text>';
    known.forEach((run, index) => {
      const y = 48 + index * 62;
      svg += '<text x="8" y="' + (y + 16) + '">' + E(Label(run)) + '</text>';
      if (Number.isFinite(run.duration)) svg += '<rect x="160" y="' + y + '" width="' + Math.max(0, run.duration / max * 735) + '" height="24" fill="#303846"' + (run.complete ? '' : ' stroke="#f3a4a4" stroke-dasharray="4 3"') + '/>';
      run.visits.forEach(visit => {
        const start = visit.start?.elapsed_seconds ?? visit.end?.start_elapsed_seconds;
        const end = visit.end?.end_elapsed_seconds;
        if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return;
        svg += '<rect x="' + x(start - run.start.elapsed_seconds) + '" y="' + y + '" width="' + ((end - start) / max * 735) + '" height="24" fill="' + (Customization.COLORS[visit.part] || '#687486') + '"><title>' + E(Customization.PARTS[visit.part] || visit.part) + ' ' + F(start - run.start.elapsed_seconds, 3) + '–' + F(end - run.start.elapsed_seconds, 3) + '초</title></rect>';
      });
      svg += '<text x="160" y="' + (y + 43) + '">' + (run.complete ? '정상 종료' : '미완료') + ' · 전체 ' + V(run.duration) + '초 / 포커스 ' + V(run.focused) + '초</text>';
    });
    return '<div class="chart">' + svg + '</svg></div><div class="legend">' + Object.entries(Customization.PARTS).map(([part, label]) => '<span><i class="dot" style="background:' + Customization.COLORS[part] + '"></i>' + E(label) + '</span>').join('')
      + '<span><i class="dot" style="background:#303846;border:1px solid #687486"></i>회차 전체 시간 · 색상 방문 구간 밖의 회색은 항목 방문 기록이 없는 시간</span>'
      + (known.some(run => run.visits.some(visit => !Customization.COLORS[visit.part])) ? '<span><i class="dot" style="background:#687486"></i>알 수 없는 항목의 방문</span>' : '')
      + (known.some(run => !run.complete) ? '<span><i class="dot" style="background:#303846;border:1px dashed #f3a4a4"></i>점선 테두리: 미완료 회차</span>' : '') + '</div>';
  }

  /** 편집 타임라인과 정상 종료 Final·저장 기록을 별도로 표시한다. */
  function RunView(runs) {
    let html = Panel('회차별 편집 흐름', Timeline(runs), '회색 바탕은 회차 전체 시간이며, 확인된 항목 방문을 색상으로 겹쳐 표시합니다. 회색으로 남은 구간에는 항목 방문 기록이 없습니다. 미종료 방문 길이와 포커스 제외 시간의 위치는 추정하지 않습니다.');
    html += Panel('방문 구간 수치', Table(['회차', '항목', '시작 / 종료(상대 초)', '전체 / 포커스(초)', '종료 사유'], runs.flatMap(run => run.visits.map(visit => {
      const start = run.start?.elapsed_seconds, from = visit.start?.elapsed_seconds ?? visit.end?.start_elapsed_seconds, to = visit.end?.end_elapsed_seconds;
      return [E(Label(run)), E(Customization.PARTS[visit.part] || visit.part), Number.isFinite(start) ? V(Number.isFinite(from) ? from - start : null) + ' / ' + V(Number.isFinite(to) ? to - start : null) : '미기록', V(visit.duration, 6) + ' / ' + V(visit.focused, 6), visit.end ? Value(visit.end, 'end_reason') : '미완료 · 종료 행 없음'];
    })), 'customVisits'));
    html += Panel('최종 유지 구성 · 정상 종료 직전', Table(['회차', '키캡 구성 (v1은 공통 재질)', '축', '케이스 기본 재질', '보강판', 'LED', '최종 이미지', '저장 / 복원 기록'], runs.map(run => {
      const final = run.complete ? run.snapshots.get('Final') : null, image = Customization.Images(run);
      return [E(Label(run)) + '<br>' + (run.complete ? '정상 종료' : '미완료 · 선호도 집계 제외'), ...['Keycap', 'Switch', 'Case', 'Plate', 'LED'].map(part => V(part === 'Keycap' && run.version >= 2 ? Customization.KeycapComposition(run) : final?.get(part)?.element_id)),
        '케이스 ' + (image.caseRetained === null ? '상태 미기록' : image.caseRetained ? '유지' : '없음') + '<br>' + (image.keycaps === null ? '키캡 상태 미기록' : '키캡 ' + V(image.keycaps, 0) + '개 (캔버스 ' + image.canvas + ' / 개별 ' + image.individual + ')'),
        run.rows.filter(row => row.record_type === 'Save').length + ' / ' + run.rows.filter(row => row.record_type === 'Load').length];
    }), 'customFinal'), 'Final은 저장 완료를 뜻하지 않습니다. 이미지가 덮인 표면의 재질은 기본 재질 정보입니다. 미완료 회차의 Interrupted 상태를 최종 선호도로 합산하지 않습니다.');
    html += Panel('초기 상태 유지와 편집 중 값 변경 횟수', Table(['회차', '편집 중 값 변경 횟수', '재선택', '초기와 같은 최종 파츠 / 확인된 파츠', 'LED 조절', '정상 종료 사유'], runs.map(run => {
      const initial = run.snapshots.get('Initial'), final = run.complete ? run.snapshots.get('Final') : null;
      const a = part => part === 'Keycap' && run.version >= 2 ? Customization.KeycapComposition(run, 'Initial', true) : initial?.get(part)?.element_id;
      const b = part => part === 'Keycap' && run.version >= 2 ? Customization.KeycapComposition(run, 'Final', true) : final?.get(part)?.element_id;
      const eligible = ['Keycap', 'Switch', 'Case', 'Plate', 'LED'].filter(part => a(part) != null && b(part) != null);
      return [E(Label(run)), V(Customization.Selections(run.selected).changed, 0),
        V(Customization.Selections(run.selected).reselected, 0),
        eligible.length ? eligible.filter(part => a(part) === b(part)).length + ' / ' + eligible.length : '미기록', run.rows.filter(row => row.record_type === 'Adjustment').length, Value(run.end, 'end_reason')];
    }), 'customInitial'));
    html += Panel('스냅샷 · 저장·복원·이미지 동작 상세', Table(['회차', '시각', '기록 / 계기', '항목', '값', '성공', 'details 원문'], runs.flatMap(run => run.rows.filter(row => ['CustomizationSnapshot', 'Save', 'Load'].includes(row.record_type)).map(row => () => [E(Label(run)), Value(row, 'elapsed_seconds', 6), V(row.record_type) + ' / ' + V(row.end_reason), E(Customization.ELEMENT_PARTS[row.part] || Customization.PARTS[row.part] || row.part || '해당 없음'), V(row.element_id), row.apply_success === true ? '성공' : row.apply_success === false ? '실패' : '해당 없음', V(row.details)])), 'customSnapshots'), '이미지 렌더러 인덱스는 물리 키 이름으로 변환하지 않습니다. details는 JSON으로 가정하지 않습니다.');
    return html;
  }

  /** 항목과 빌드별 제공 요소를 선택 시도·편집 중 값 변경 횟수·재선택·최종 유지와 비교한다. */
  function Elements(runs) {
    const elements = Customization.Elements(runs, c.part);
    const max = Math.max(1, ...elements.map(element => element.attempts));
    const filterParts = Object.entries(Customization.ELEMENT_PARTS).filter(([part]) => part !== 'PlateMaterial');
    let html = '<label class="custom-part-filter">항목 <select id="customPartSelect">' + filterParts.map(([part, label]) => '<option value="' + part + '" ' + (c.part === part ? 'selected' : '') + '>' + E(label) + '</option>').join('') + '</select></label>';
    if (c.part === 'Keycap') html += KeycapAnalysis(runs);
    html += Panel('요소 선택 · 변경과 재선택', elements.length ? elements.map(element => '<div class="stack-row"><div><b>' + V(element.element) + '</b><span>선택 시도 ' + element.attempts + '회 · 빌드 ' + V(element.build) + '</span></div><div class="stack"><span style="width:' + element.changed / max * 100 + '%;background:' + Customization.COLORS[c.part] + '"></span><span style="width:' + element.reselected / max * 100 + '%;background:' + Customization.COLORS[c.part] + ';opacity:.3"></span></div><small>편집 중 값 변경 횟수 ' + V(element.changed, 0) + ' / 같은 요소 재선택 ' + V(element.reselected, 0) + ' / 실패 ' + V(element.failed, 0) + '</small></div>').join('') : Empty('제공 목록·요소 선택 미기록. 이미지 동작과 LED 연속 조절은 다른 화면에서 확인하세요.'), '진한 막대는 편집 중 값 변경 횟수, 옅은 막대는 재선택입니다. 제공됐지만 선택 0회인 요소도 포함합니다.');
    html += Panel('요소별 분모와 최종 유지', Table(['요소 / 빌드', '제공 회차', '선택 시도 / 성공', '선택 비중', '선택 플레이어 / 제공 플레이어', '제공 기준 최종 유지율', '상세'], elements.map(element => [V(element.element) + ' / ' + V(element.build), element.offered || '제공 미기록', element.attempts + ' / ' + V(element.success, 0), Rate(element.attempts, element.denominator), Rate(element.selectedTesters, element.offeredTesters), Rate(element.retained, element.eligible), '<button data-custom-element="' + E(element.element) + '">선택 상세</button>']), 'customElements'), '선택 비중은 같은 파츠·빌드 전체 ElementSelected를 분모로 합니다. 플레이어 비율은 파일 식별자의 중복을 제거합니다. 최종 비율은 해당 요소가 제공되고 Final 상태가 확인된 완료 회차만 분모로 합니다. 보강판은 외형 변경이며 타건음 변경을 뜻하지 않습니다.');
    if (c.element) html += Panel('선택 요소: ' + c.element, Table(['회차', '앱 시각', '이전 → 선택', '적용', '편집 중 값 변경 횟수', 'details'], runs.flatMap(run => run.selected.filter(row => Customization.SameElementPart(row.part, c.part) && row.element_id === c.element).map(row => [E(Label(run)), Value(row, 'elapsed_seconds', 6), V(row.previous_element_id) + ' → ' + V(row.element_id), Value(row, 'apply_success'), Value(row, 'value_changed'), V(row.details)])), 'customElementDetail'));
    const adjustments = runs.flatMap(run => run.rows.filter(row => row.record_type === 'Adjustment'));
    html += Panel('LED 조절 · 요소 버튼과 별도', Table(['조절 종류', '연속 변경 이벤트 수'], [...Playtest.Group(adjustments, row => row.element_id)].map(([kind, rows]) => [V(kind), rows.length]), 'customAdjust'), '예를 들어 Color 122건은 버튼 클릭 122회를 의미하지 않습니다.');
    return html;
  }

  /** 이미지 동작별 시도와 실제 대상 적용 및 완료 후 유지율을 반환한다. */
  function ImageView(runs) {
    const rows = runs.flatMap(run => run.rows.filter(row => row.record_type === 'ImageAction'));
    const metrics = Customization.ImageMetrics(runs);
    const scopes = { CaseWhole: '케이스 전체', SingleKeycap: '개별 키캡', AllKeycapsCanvas: '전체 키캡 캔버스' };
    let html = '<div class="metrics">' + Metric('성공 적용', rows.filter(Customization.IsImageApply).length + '회', 'Apply·Replace AND apply_success=true') + Metric('이미지 사용률', Rate(metrics.used, metrics.offered), '성공 적용 회차 / 이미지 제공 회차') + Metric('이미지 최종 유지율', metrics.retention === null ? '미기록' : Rate(metrics.retained, metrics.completedUsed), '성공 적용이 있었던 완료 회차 · 상태 확인 ' + metrics.known + '/' + metrics.completedUsed) + '</div>';
    html += Panel('이미지 적용 대상', Bars(Object.entries(scopes).map(([scope, label]) => ({ label, color: Customization.COLORS.CustomImage, segments: [{ label: '성공 적용', value: rows.filter(row => row.target_scope === scope && Customization.IsImageApply(row)).length }] })), '회'), '케이스는 전체 단위입니다. 면별 적용이나 가짜 이미지 미리보기를 만들지 않습니다.');
    const labels = { ChooseStarted: '선택 창 시작', ChooseCancelled: '선택 창 취소', LoadSucceeded: '미리보기 읽기 성공', LoadFailed: '미리보기 읽기 실패', Apply: '적용', Replace: '교체', Remove: '제거 요청' };
    html += Panel('동작별 기록', Table(['동작', '전체', '처리 성공', '처리 실패'], Object.entries(labels).map(([action, label]) => { const items = rows.filter(row => row.element_id === action); const applicable = !['ChooseStarted', 'ChooseCancelled'].includes(action); return [E(label), items.length, applicable ? items.filter(row => row.apply_success === true).length : '해당 없음', applicable ? items.filter(row => row.apply_success === false).length : '해당 없음']; }), 'customImageActions'), '선택 창 취소는 실패가 아닙니다. LoadSucceeded는 미리보기이며 적용으로 세지 않습니다. Remove는 제거 요청 처리이며 실제 감소는 스냅샷으로 확인합니다.');
    html += Panel('이미지 원본 동작', Table(['회차', '시각', '동작', '적용 대상', '렌더러 인덱스', '처리 성공', 'details'], runs.flatMap(run => run.rows.filter(row => row.record_type === 'ImageAction').map(row => [E(Label(run)), Value(row, 'elapsed_seconds', 6), E(labels[row.element_id] || row.element_id), E(scopes[row.target_scope] || '해당 없음·미기록'), row.target_scope === 'SingleKeycap' ? Value(row, 'keycap_index', 0) : '해당 없음', Value(row, 'apply_success'), V(row.details)])), 'customImageRaw'), '인덱스는 0부터 시작하는 렌더러 번호이며 A·B 같은 키 이름이 아닙니다. 텍스처 ID는 실행 중 객체 ID로 이미지 콘텐츠나 다른 파일을 식별하지 않습니다.');
    return html;
  }

  /** 회차와 플레이어 단위의 평균·중앙값 및 항목별 체류 차이를 별도로 반환한다. */
  function Compare(runs) {
    const groups = [...Playtest.Group(runs, run => run.tester || '플레이어 미기록')];
    let html = Panel('플레이어별 비교', Table(['플레이어', '회차 / 정상 종료', '회차 전체 평균 / 중앙값', '포커스 평균 / 중앙값', '요소 적용 시도', '성공 이미지 적용', '서로 다른 객체(세션 내)'], groups.map(([tester, items]) => {
      const duration = Customization.Describe(items.map(run => run.duration)), focus = Customization.Describe(items.map(run => run.focused));
      return [E(PlayerLabel(tester)), items.length + ' / ' + items.filter(run => run.complete).length, V(duration.mean) + ' / ' + V(duration.median) + '초', V(focus.mean) + ' / ' + V(focus.median) + '초', items.reduce((sum, run) => sum + run.selected.length, 0), items.reduce((sum, run) => sum + run.imageApplies.length, 0), new Set(items.map(run => JSON.stringify([run.fileId, run.session, run.keyboard]))).size];
    }), 'customCompare'), '파일 하나를 플레이어 한 명으로 구분합니다. CSV의 tester_id는 원본 정보로만 보존합니다. 같은 플레이어의 여러 회차를 여러 사람의 선호로 세지 않습니다. keyboard_id는 실행 간 동일 객체를 보장하지 않습니다.');
    const people = groups.filter(([tester]) => tester !== '플레이어 미기록').map(([, items]) => Customization.Describe(items.map(run => run.focused)).mean);
    const all = Customization.Describe(runs.map(run => run.focused));
    html += '<div class="metrics">' + Metric('회차 기준 평균 포커스', V(all.mean) + '초', '알려진 회차 ' + all.count + ' / 미기록 ' + all.missing) + Metric('플레이어 평균의 평균', V(Customization.Describe(people).mean) + '초', '각 플레이어의 회차 평균을 같은 가중치로 비교') + '</div>';
    const stats = runs.map(run => ({ run, parts: Customization.Parts([run]) }));
    const max = Math.max(1, ...stats.flatMap(item => item.parts.map(part => part.focused || 0)));
    html += Panel('같은 눈금의 회차별 항목 포커스', stats.map(({ run, parts }) => '<div class="comparison-group"><h3>' + E(Label(run)) + ' · 세션 ' + E(run.session?.slice(0, 8) || '미기록') + '</h3>' + parts.map(part => '<div class="stack-row"><span>' + E(Customization.PARTS[part.part]) + ' · ' + V(part.focused) + '초</span><div class="stack"><span style="width:' + Math.max(0, part.focused || 0) / max * 100 + '%;background:' + Customization.COLORS[part.part] + '"></span></div></div>').join('') + '</div>').join(''), '종료된 항목 방문 기록만 사용합니다. 회차 전체 시간에 포함된 회전·감상 시간을 파츠에 임의 배분하지 않습니다.');
    return html;
  }

  /** 종류 필터와 관찰된 키캡 종류가 일치하는지 반환하며 빈 종류를 일반으로 간주하지 않는다. */
  function MatchesKeycapKind(kind) { return c.keycapKind === 'ALL' || (kind || 'MISSING') === c.keycapKind; }
  /** v2의 키캡 버튼 선택·적용·최종 장착 지표를 종류 필터와 독립 눈금 그래프로 표시한다. */
  function KeycapAnalysis(runs) {
    const modern = runs.filter(run => run.version >= 2), legacy = runs.filter(run => run.version < 2);
    let html = '<label class="custom-part-filter">키캡 종류 <select id="customKeycapKind">' + [['ALL', '전체'], ['Regular', '일반 (Regular)'], ['Artisan', '아티산 (Artisan)'], ['MISSING', '종류 미기록']].map(([value, label]) => '<option value="' + value + '" ' + (c.keycapKind === value ? 'selected' : '') + '>' + label + '</option>').join('') + '</select></label>';
    if (!modern.length) return html + Panel('키캡 v2 · 선택과 최종 장착', Empty('v2 회차 없음 · v1에는 파츠 종류·버튼 선택·개별 장착 정보가 미수집입니다.'), 'v1 재질 선택을 일반/아티산 선택으로 추정하지 않습니다.');
    const stats = Customization.KeycapStats(modern).filter(item => MatchesKeycapKind(item.kind));
    const kinds = ['Regular', 'Artisan', null].filter(MatchesKeycapKind).map(kind => {
      const items = stats.filter(item => item.kind === kind), known = modern.some(run => run.complete && [...Customization.KeyStates(run).values()].some(row => typeof row.is_installed === 'boolean'));
      return { label: kind || '종류 미기록', picked: items.reduce((sum, item) => sum + item.picked, 0), installed: known ? items.reduce((sum, item) => sum + (item.installed ?? 0), 0) : null };
    });
    html += Panel('키캡 종류별 버튼 선택', Bars(kinds.map(item => ({ label: item.label, color: item.label === 'Artisan' ? '#ffad32' : '#38a9ff', segments: [{ label: '버튼 선택', value: item.picked }] })), '회'), '현재 필터 범위의 완료·중단 회차에서 ElementPicked만 집계합니다. 호환용 ElementSelected와 적용 조작은 합산하지 않습니다.');
    html += Panel('키캡 종류별 최종 장착', Bars(kinds.map(item => ({ label: item.label, color: item.label === 'Artisan' ? '#ffad32' : '#38a9ff', segments: [{ label: 'Final 장착', value: item.installed }] })), '키'), '정상 종료 Final의 is_installed=true인 개별 키만 집계합니다. 선택 횟수와 장착 키 수는 단위가 달라 별도 그래프입니다. 일반/아티산은 전투 효과 구분이 아닙니다.');
    const completed = modern.filter(run => run.complete), known = completed.filter(run => Customization.KeyStates(run).size);
    html += Panel('v2+ 파츠 ID별 선택 · 적용 · 최종 장착', Table(['파츠 ID / 종류 / 빌드', '버튼 선택(회)', '적용 시도 / 성공 / 실패(회)', '선택 플레이어 / 회차', '최종 장착(키)', '최종 장착 플레이어 / 회차'], stats.map(item => { const cells = [KeycapValue(item.element) + '<br>' + V(item.kind) + '<br>빌드 ' + V(item.build), item.picked, item.attempts + ' / ' + V(item.success, 0) + ' / ' + V(item.failed, 0), item.players + '명 / ' + item.runs + '회차', V(item.installed, 0), V(item.installedPlayers, 0) + '명 / ' + V(item.installedRuns, 0) + '회차']; return item.picked === 0 && item.attempts === 0 && item.installed === 0 ? cells.map(cell => '<span class="muted">' + cell + '</span>') : cells; }), 'customKeycapV2'), '전체 적용도 조작 1회입니다. 버튼 선택 플레이어는 파일 기준으로 중복 제거합니다. 장착 집계는 완료 회차에서 관찰된 Final 키 범위이며 종료 전 상태나 Interrupted를 합치지 않습니다. 완료 ' + completed.length + '회차 중 개별 Final 확인 ' + known.length + ', Final 미기록 ' + (completed.length - known.length) + ', 중단 ' + (modern.length - completed.length) + ', v1 제외 ' + legacy.length + '회차. 빈 ID·종류·장착 여부는 미기록으로 유지합니다.');
    return html;
  }
  /** v2 키 인덱스별 초기·최종 정의와 장착 여부를 원본 상태 그대로 비교한다. */
  function KeycapDetails(runs) {
    const modern = runs.filter(run => run.version >= 2);
    if (!modern.length) return '';
    return Panel('v2+ 개별 키 · 초기 → 최종', modern.map(run => {
      const initial = Customization.KeyStates(run, 'Initial'), final = Customization.KeyStates(run);
      const keys = [...new Set([...initial.keys(), ...final.keys()])].sort((a, b) => a - b);
      const rows = keys.filter(index => c.keycapKind === 'ALL' || initial.has(index) && MatchesKeycapKind(initial.get(index)?.keycap_kind) || final.has(index) && MatchesKeycapKind(final.get(index)?.keycap_kind)).map(index => () => {
        const a = initial.get(index), b = final.get(index);
        return [index, KeycapValue(a?.element_id), V(a?.keycap_kind), V(a?.material_id), typeof a?.is_installed === 'boolean' ? String(a.is_installed) : '미기록', KeycapValue(b?.element_id), V(b?.keycap_kind), V(b?.material_id), typeof b?.is_installed === 'boolean' ? String(b.is_installed) : '미기록', !Customization.KeyKnown(a) || !Customization.KeyKnown(b) ? '판정 불가' : Customization.KeySignature(a) === Customization.KeySignature(b) ? '최종 상태 동일' : '<span class="badge good">변경</span>'];
      });
      return '<div class="comparison-group"><h3>' + E(Label(run)) + ' · ' + (run.complete ? '정상 종료' : '중단 · Final 집계 제외') + '</h3><p>초기 관찰 ' + initial.size + '키 / 최종 관찰 ' + final.size + '키 · 키 인덱스는 로그 번호입니다.</p>' + Table(['키 인덱스', '초기 파츠 ID', '초기 종류', '초기 재질', '초기 장착', '최종 파츠 ID', '최종 종류', '최종 재질', '최종 장착', '비교'], rows, 'customKeycapDetails' + run.key) + '</div>';
    }).join(''), 'snapshot_id로 구분된 스냅샷만 비교합니다. 공통 Keycap 재질 행은 개별 장착 상태를 대신하지 않습니다. 같은 최종 상태만으로 편집 중 변경이 없었다고 단정하지 않습니다.');
  }

  /** 정상 종료 회차의 초기·최종 상태와 탐색·이미지·저장·조합을 기존 필터 안에서 표시한다. */
  function Results(runs) {
    const completed = runs.filter(run => run.complete), interrupted = runs.filter(run => !run.complete);
    const names = { ...Customization.PARTS, ...Customization.ELEMENT_PARTS, LED: 'LED 패턴', LEDColor: 'LED 색상', LEDEnabled: 'LED 켜짐' };
    const partName = part => names[part] || part;
    const target = part => part === 'Image:CaseWhole' ? '케이스 전체' : part.startsWith('Image:Keycap:') ? '키캡 렌더러 #' + part.slice(13) : partName(part);
    let html = '<div class="metrics">' + Metric('정상 종료 집계', completed.length + '회차', 'Interrupted는 최종 구성 집계 제외')
      + Metric('플레이어', new Set(completed.map(run => run.tester).filter(Boolean)).size + '명', '정상 종료 회차가 있는 파일 수')
      + Metric('키보드', new Set(completed.filter(run => run.keyboard).map(run => JSON.stringify([run.fileId, run.session, run.keyboard]))).size + '대', '세션 내 keyboard_id 중복 제거')
      + Metric('중단·미완료', interrupted.length + '회차', '현재 공통 필터 범위') + '</div>';
    if (interrupted.length) html += Panel('중단 회차 · 집계 제외', Table(['회차', '종료 사유', '종료 상태'], interrupted.map(run => [E(Label(run)), V(run.end?.end_reason), '미완료 · Interrupted를 Final로 대체하지 않음']), 'resultsInterrupted'));
    const formatPartValue = (part, value) => part === 'LEDColor' && /^[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value || '')
      ? '<i class="dot" style="background:#' + E(value.slice(0, 6)) + ';border:1px solid #687486;vertical-align:-1px"></i>' + V(value) : V(value);
    const resultLabels = { changed: '변경' };
    const isChanged = status => ['변경 유지', '변경'].includes(status);
    const statusBadge = (status, reason) => isChanged(status) ? '<span class="badge good">' + E(resultLabels.changed) + '</span>'
      : status === '원복' ? '<span class="badge warn">원복</span>'
      : status === '판정 불가' ? '<span class="badge bad" tabindex="0" style="cursor:help" title="' + E(reason || '초기·최종 상태 또는 변경 경로를 확인할 기록이 부족합니다.') + '" aria-label="판정 불가: ' + E(reason || '기록 부족') + '">판정 불가 ⓘ</span>'
      : '<span class="muted">' + E(status) + '</span>';
    html += KeycapAnalysis(runs);
    html += Panel('초기 → 최종 · 파츠별 결과', completed.map(run => {
      const parts = Customization.FinalParts(run), base = parts.slice(0, 6), known = base.filter(item => item.before !== null && item.after !== null && (run.version < 2 || item.part !== 'Keycap' || item.finalDifferent !== null));
      const order = ['Keycap', 'Switch', 'LED', 'LEDColor', 'LEDEnabled', 'Case', 'Plate', 'PlateEffect'];
      const orderedParts = order.map(part => parts.find(item => item.part === part)).filter(Boolean);
      const imageValue = (value, scope, phase) => value === null
        ? '<span tabindex="0" style="cursor:help" title="' + E(phase + ' ' + scope + ' 이미지 상태가 미기록이거나 일부 대상만 확인되어 사용 여부를 확정할 수 없습니다.') + '">판정 불가 ⓘ</span>' : String(value);
      const imageRows = Customization.ImageResults(run).map(item => {
        const changed = isChanged(item.status), dim = item.status === '초기 유지';
        const wrap = (text, highlight = false) => highlight ? '<b style="color:var(--accent)">' + text + '</b>' : dim ? '<span class="muted">' + text + '</span>' : text;
        const changeCount = item.changed === null ? '<span title="성공 동작 전후 이미지 스냅샷이 부족해 값 변경 횟수를 확정할 수 없습니다.">미기록 ⓘ</span>' : V(item.changed, 0);
        const loadNote = item.loadChanged ? '<small class="image-load-note" tabindex="0" title="Load로 이미지 상태가 변경되었습니다. 복원은 직접 적용 시도·성공·변경 횟수에 포함하지 않습니다.">복원으로 변경 ⓘ</small>' : '';
        return [wrap(E('커스텀 이미지 (' + item.scope + ')'), changed), wrap(imageValue(item.before, item.scope, '초기')), wrap(imageValue(item.after, item.scope, '최종'), changed), wrap(item.attempts), wrap(V(item.success, 0)), wrap(changeCount), '—', statusBadge(item.status, item.reason) + loadNote];
      });
      return '<div class="comparison-group"><h3>' + E(Label(run)) + ' · 빌드 ' + V(run.build) + '</h3><p>초기 대비 최종 변경 파츠 수: ' + known.filter(item => item.finalDifferent ?? item.before !== item.after).length + ' / 비교 가능 ' + known.length + '개 (기본 6파츠) · LED 색상·켜짐은 아래 별도 비교</p>'
        + Table(['파츠', '초기값', '최종값', '선택·조절 시도', '성공', '편집 중 값 변경 횟수', '동일값 재선택', '결과 상태'], orderedParts.map(item => {
          const changed = isChanged(item.status), dim = item.status === '초기 유지';
          const wrap = (text, highlight = false) => highlight ? '<b style="color:var(--accent)">' + text + '</b>' : dim ? '<span class="muted">' + text + '</span>' : text;
          const loadNote = item.loadChanged ? '<small class="image-load-note" tabindex="0" title="Load로 이 파츠의 값이 변경되었습니다. 복원은 직접 선택·조절의 시도·성공·변경 횟수에 포함하지 않습니다.">복원으로 변경 ⓘ</small>' : '';
          return [wrap(E(partName(item.part)), changed), wrap(formatPartValue(item.part, item.before)), wrap(formatPartValue(item.part, item.after), changed), wrap(item.attempts), wrap(V(item.success, 0)), wrap(V(item.changed, 0)), wrap(V(item.reselected, 0)), statusBadge(item.status, item.reason) + loadNote];
        }).concat(imageRows), 'resultsParts' + run.key) + '</div>';
    }).join('') || Empty('정상 종료 회차 없음'), '초기 유지는 시작·종료, 연속 sequence, 성공 선택의 이전→이후 연결까지 확인한 경우입니다. 같은 최종값이라도 기록이 불완전하면 판정 불가입니다. 원복은 초기와 다른 값으로 성공 변경한 증거가 필요합니다. LED 색상·켜짐 행은 선택 버튼 대신 Adjustment를 집계하며 밝기·속도는 추정하지 않습니다. 이미지 시도·성공은 Apply/Replace/Remove 동작 수이며, 변경은 전후 상태가 실제로 달라진 동작 수입니다. 전체 키캡 적용도 1회입니다. 미리보기·저장·복원은 이 횟수에서 제외합니다.');
    html += KeycapDetails(runs);
    const elements = Customization.FinalElements(completed);
    const maximum = Math.max(1, ...elements.flatMap(item => [item.attempts, item.retained]));
    html += Panel('탐색 빈도와 최종 유지', elements.map(item => '<div class="result-exploration"><b>' + E(partName(item.part)) + ' · ' + V(item.element) + ' · 빌드 ' + V(item.build) + '</b><div>선택 시도 ' + item.attempts + '회<div class="stack"><span style="width:' + item.attempts / maximum * 100 + '%;background:#38a9ff"></span></div></div><div>Final 유지 ' + item.retained + '회차<div class="stack"><span style="width:' + item.retained / maximum * 100 + '%;background:#ffad32"></span></div></div></div>').join(''), '두 막대는 공통 눈금입니다. 선택 횟수와 회차 수를 합산하지 않습니다. 최종 유지에는 초기 기본값 유지도 포함됩니다.');
    html += Panel('요소별 탐색 → 최종 구성', Table(['파츠 / 요소 / 빌드', '선택 시도 / 성공(회)', '선택 플레이어 / 회차', 'Final 유지(회차)', '직접 성공 선택 후 유지', '초기부터 변경 없이 유지', '초기와 다른 최종값', '최종 구성 비중', '선택 후 최종 유지율'], elements.map(item => { const cells = [E(partName(item.part)) + '<br>' + V(item.element) + '<br>빌드 ' + V(item.build), item.attempts + ' / ' + V(item.success, 0), item.testers + '명 / ' + item.selected + '회차', item.retained, item.direct, item.initial, item.finalChanged, Rate(item.retained, item.denominator), Rate(item.direct, item.selectedKnown)]; return item.attempts === 0 && item.finalChanged === 0 ? cells.map(cell => '<span class="muted">' + cell + '</span>') : cells; }), 'resultsElements'), '정상 종료 회차만 집계합니다. 최종 구성 비중 = 해당 요소 Final 회차 / 같은 파츠 Final 확인 회차. 선택 후 최종 유지율 = 성공 선택 후 같은 요소 Final 회차 / 성공 선택했고 같은 파츠 Final 확인 회차. 빌드별 분모를 사용합니다. 제공 기준 유지율은 기존 요소 선택 탭에 별도로 유지합니다. 같은 파일의 여러 회차는 플레이어 한 명으로 셉니다.');
    html += Panel('저장 상태와 편집 종료 상태', Table(['회차', '저장 시도 / 성공 / 실패 / 미기록', '복원 시도 / 성공 / 실패 / 미기록', '마지막 성공 저장(앱 초)', 'Final 비교', '달라진 대상', '비교 불가 대상'], completed.map(run => {
      const storage = Customization.Storage(run), stats = value => [value.attempts, value.success, value.failed, value.unknown].join(' / ');
      return [E(Label(run)), stats(storage.save), stats(storage.load), V(storage.last?.elapsed_seconds, 6), E(storage.status), storage.different.length ? storage.different.map(part => E(target(part))).join('<br>') : '확인된 차이 없음', storage.last ? storage.unknown.map(part => E(target(part))).join('<br>') : '저장 상태 비교 대상 없음'];
    }), 'resultsStorage'), 'Final은 편집 종료 상태이며 저장 완료를 뜻하지 않습니다. 성공 저장 바로 뒤의 Save:요소 스냅샷 묶음만 연결합니다. 반복 사유는 시점별로 분리하며 sequence 누락·연결 모호·이미지 미기록이면 전체 동일 판정을 생략합니다.');
    const combinations = Customization.Combinations(completed);
    const comboTable = (items, key) => Table(['조합 / 빌드', '회차 수', '고유 플레이어', '비중'], items.map(item => [V(item.display ?? item.value) + '<br>빌드 ' + V(item.build), item.count, item.testers, Rate(item.count, item.denominator)]), key);
    html += Panel('최종 기본 파츠 조합', comboTable(combinations.basic, 'resultsCombinations'), '키캡(v2 개별 인덱스 구성 / v1 공통 재질) + 축 + 케이스 + 보강판 + LED 패턴. 필수값 누락 제외 ' + combinations.excluded + '회차. 비중 분모는 같은 빌드의 완전한 조합 회차입니다. 동일 플레이어 반복 편집도 회차 수에 반영됩니다.');
    return html;
  }

  /** 독립 필터 상태를 동기화하고 현재 커스터마이징 분석 탭을 표시한다. */
  function Render() {
    Sync();
    document.getElementById('pageTitle').textContent = titles[c.tab][0];
    document.getElementById('pageSubtitle').textContent = titles[c.tab][1];
    document.getElementById('breadcrumb').textContent = '커스터마이징 / ' + titles[c.tab][0];
    document.querySelectorAll('[data-custom-tab]').forEach(button => { button.classList.toggle('active', button.dataset.customTab === c.tab); button.setAttribute('aria-pressed', String(button.dataset.customTab === c.tab)); });
    const host = document.getElementById('content');
    document.getElementById('welcome').hidden = c.files.length > 0;
    if (!c.files.length) { host.replaceChildren(); return; }
    const runs = Runs();
    const filters = '<div class="controls"><label><input id="customCompletedOnly" type="checkbox" ' + (c.completedOnly ? 'checked' : '') + '> 정상 완료 회차만</label><label><input id="customExcludeEmpty" type="checkbox" ' + (c.excludeEmpty ? 'checked' : '') + '> 요소 선택·이미지 적용 없는 회차 제외</label><span class="hint">현재 ' + runs.length + '회차 · ' + (c.tab === 'results' ? '최종 결과물은 정상 종료만 집계 · 중단은 별도 표시' : '기본은 무선택·중단 포함 전체') + ' · LED Adjustment만 있는 회차도 이 제외 조건의 대상</span></div>';
    host.innerHTML = filters + (!runs.length ? Empty('현재 필터에 해당하는 편집 회차 없음') : ({ summary: Summary, runs: RunView, elements: Elements, images: ImageView, compare: Compare, results: Results })[c.tab](runs));
  }
  /** 추가 파일을 같은 파일 식별자로 갱신하고 데이터 유형 전용 저장소에 보존한다. */
  function Add(file) { const index = c.files.findIndex(item => item.id === file.id); if (index >= 0) c.files[index] = file; else c.files.push(file); }
  /** 선택 파일을 제거하되 다른 데이터 유형의 파일과 필터는 변경하지 않는다. */
  function Remove() { const id = c.file === 'ALL' ? c.files[0]?.id : c.file; c.files = c.files.filter(file => file.id !== id); c.file = 'ALL'; c.run = 'ALL'; }
  /** 커스터마이징 클릭을 탭·회차·요소 선택 상태에 반영하고 처리 여부를 반환한다. */
  function Click(button) {
    if (button.dataset.customTab) { c.tab = button.dataset.customTab; return true; }
    if (button.dataset.customRun) { c.run = button.dataset.customRun; c.tab = button.dataset.customTarget === 'results' ? 'results' : 'runs'; return true; }
    if (button.dataset.customElement) { c.element = button.dataset.customElement; return true; }
    return false;
  }
  /** 선택·체크 입력을 커스터마이징 전용 필터에 반영한다. */
  function Change(target) {
    if (target.id === 'customKeycapKind') c.keycapKind = target.value;
    if (target.id === 'customFileSelect') { c.file = target.value; c.tester = c.session = c.run = 'ALL'; }
    if (target.id === 'testerSelect') { c.tester = target.value; c.session = c.run = 'ALL'; }
    if (target.id === 'customSessionSelect') { c.session = target.value; c.run = 'ALL'; }
    if (target.id === 'customRunSelect') c.run = target.value;
    if (target.id === 'customPartSelect') { c.part = target.value; c.element = ''; }
    if (target.id === 'customCompletedOnly') c.completedOnly = target.checked;
    if (target.id === 'customExcludeEmpty') c.excludeEmpty = target.checked;
  }
  return { Render, Add, Remove, Click, Change, Runs, Count: () => c.files.length };
})();


