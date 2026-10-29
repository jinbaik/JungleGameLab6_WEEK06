"use strict";

const state = { mode: "playtest", files: [], file: "", tournament: "ALL", battle: "", tab: "overview", scope: "playable", board: "", compareBoard: "", key: "", owner: "Player", interval: 5, pages: {}, compareIncomplete: false };
const titles = { overview: ["실행 개요", "대전과 상점, 키보드 보충을 하나의 흐름으로 읽습니다."], battle: ["대전 분석", "입력량과 점수, 콤보 구간에 기록된 변화를 확인합니다."], keyboard: ["키보드 · 키 상세", "같은 눈금으로 입력 패턴과 실제 기믹 배치를 살펴봅니다."], shop: ["기믹 · 상점", "보유 변화부터 구매 확정과 효과 실행까지 추적합니다."], compare: ["실행 비교", "시간과 완료 조건을 구분하여 여러 실행을 나란히 비교합니다."] };
const TYPE_COLORS = { None: "#637185", Explosion: "#f3a4a4", Bonus: "#e8d486", Row: "#b9ec82", Column: "#b5a0f2", All: "#eeae7c" };

/** 값을 HTML 텍스트로 이스케이프하여 외부 CSV 내용의 실행을 방지한다. */
function E(value) { return String(value ?? "미수집").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char])); }
/** 값과 소수점 자릿수를 표시하고, 빈 값과 숫자 0을 구분한 문자열을 반환한다. */
function F(value, digits = 0) { return value === null || value === undefined ? "미수집" : typeof value === "number" ? value.toLocaleString("ko-KR", { maximumFractionDigits: digits }) : E(value); }
/** 행의 원본 상태를 사용하여 잘못된 값과 미수집을 구분한 셀을 반환한다. */
function Field(row, field, digits = 3) { return row?.status[field] === "invalid" ? '잘못된 값: ' + E(row.raw[field]) : F(row?.[field], digits); }
/** 선택된 파일을 현재 상태에서 찾아 반환한다. */
function Dataset() { return state.files.find(file => file.id === state.file); }
/** 선택 실행의 토너먼트 범위에 속한 대전 목록을 반환한다. */
function Runs() { const file = Dataset(); return !file ? [] : state.tournament === "ALL" ? file.runs : file.tournaments.find(tournament => tournament.key === state.tournament)?.runs || []; }
/** 선택 토너먼트의 이벤트 또는 파일 전체 이벤트를 반환한다. */
function Rows() { const file = Dataset(); return !file ? [] : state.tournament === "ALL" ? file.events : file.tournaments.find(tournament => tournament.key === state.tournament)?.rows || []; }
/** 현재 선택 대전을 찾아 반환한다. */
function Battle() { return Runs().find(run => run.key === state.battle); }
/** 식별자와 대전 번호를 사용하여 정규전·연장전이 구별되는 라벨을 반환한다. */
function RunLabel(run) { return (run.round === null || run.round === undefined ? "번호 미수집" : run.round + "R") + " · " + (run.kind || "종류 미수집"); }
/** 대전 완료와 승패에 따라 텍스트를 동반한 상태 배지를 반환한다. */
function Badge(run) { return '<span class="badge ' + (!run.complete ? 'warn' : run.outcome === '패배' ? 'bad' : 'good') + '">' + E(run.outcome) + '</span>'; }
/** 설명을 포함한 요약 숫자 카드 마크업을 반환한다. */
function Metric(label, value, note) { return '<div class="metric"><label>' + E(label) + '</label><strong>' + value + '</strong><small>' + E(note) + '</small></div>'; }
/** 제목과 내용을 기존 카드 스타일로 감싼 마크업을 반환한다. */
function Panel(title, body, note = "") { return '<section class="panel"><h2>' + E(title) + '</h2>' + (note ? '<p class="hint">' + E(note) + '</p>' : '') + body + '</section>'; }
/** 내용이 없는 상태를 사용자에게 설명하는 마크업을 반환한다. */
function Empty(message) { return '<div class="empty">' + E(message) + '</div>'; }
/** 표를 35행 단위로 나누어 생성하고 페이지 이동 정보를 반환한다. */
function Table(headers, rows, key) {
  if (!rows.length) return Empty("해당 기록 없음");
  const size = 35, pages = Math.ceil(rows.length / size), page = Math.min(state.pages[key] || 0, pages - 1);
  state.pages[key] = page;
  return '<div class="table-wrap"><table><thead><tr>' + headers.map(header => '<th>' + E(header) + '</th>').join('') + '</tr></thead><tbody>' + rows.slice(page * size, (page + 1) * size).map(source => { const row = typeof source === 'function' ? source() : source; return '<tr>' + row.map(cell => '<td>' + cell + '</td>').join('') + '</tr>'; }).join('') + '</tbody></table></div>' +
    (pages > 1 ? '<div class="table-page"><button data-page="' + E(key) + '" data-delta="-1" ' + (!page ? 'disabled' : '') + '>이전</button><span>' + (page + 1) + ' / ' + pages + ' · ' + rows.length + '행</span><button data-page="' + E(key) + '" data-delta="1" ' + (page === pages - 1 ? 'disabled' : '') + '>다음</button></div>' : '');
}
/** 분모가 명확한 일반키·기믹키·분류불가 누적 막대와 실제 수치를 반환한다. */
function InputStack(label, input, maximum) {
  if (input.normal === null) return '<div class="stack-row">' + E(label) + ' · 유형별 분류 미수집 · 원본 Down ' + F(input.raw) + '</div>';
  const total = input.normal + input.special + input.unknown;
  return '<div class="stack-row"><div><b>' + E(label) + '</b><span>일반 ' + F(input.normal) + ' / 기믹 ' + F(input.special) + ' / 분류 불가 ' + F(input.unknown) + '</span></div><div class="stack"><span class="normal-fill" style="width:' + input.normal / maximum * 100 + '%"></span><span class="special-fill" style="width:' + input.special / maximum * 100 + '%"></span><span class="unknown-fill" style="width:' + input.unknown / maximum * 100 + '%"></span></div><small class="muted">분류된 입력 중 기믹 비중 ' + (input.normal + input.special ? F(input.special / (input.normal + input.special) * 100, 1) + '%' : '해당 없음') + ' · 현재 범위 합계 ' + total + '</small></div>';
}
/** 기록된 점·구간과 선택적인 이벤트 표시점을 공통 SVG 좌표에 그려 차트와 범례를 반환한다. */
function Plot(series, unit, step = false, markers = []) {
  const valid = series.filter(item => item.points.some(point => Number.isFinite(point.x) && Number.isFinite(point.y)));
  if (!valid.length) return Empty("이 범위의 " + unit + " 기록이 없습니다. 값을 추정하지 않습니다.");
  const points = valid.flatMap(item => item.points).filter(point => Number.isFinite(point.x) && Number.isFinite(point.y));
  const maxX = Math.max(1, ...points.map(point => point.x), ...markers.map(marker => marker.x)), maxY = Math.max(1, ...points.map(point => point.y)) * 1.12;
  const x = value => 64 + value / maxX * 766, y = value => 235 - value / maxY * 205;
  const markerSpace = markers.length ? 48 : 0;
  let svg = '<svg viewBox="0 0 860 ' + (285 + markerSpace) + '" role="img" aria-label="' + E(unit) + ' 시간별 그래프"><g transform="translate(0,' + markerSpace + ')"><text x="12" y="16">' + E(unit) + '</text>';
  for (let index = 0; index <= 4; index++) {
    svg += '<line class="grid" x1="64" x2="830" y1="' + y(maxY * index / 4) + '" y2="' + y(maxY * index / 4) + '"/><text x="54" text-anchor="end" y="' + (y(maxY * index / 4) + 4) + '">' + F(maxY * index / 4, 0) + '</text>';
    svg += '<text x="' + x(maxX * index / 4) + '" y="256" text-anchor="middle">' + F(maxX * index / 4, 1) + '</text>';
  }
  valid.forEach(item => {
    let path = '', previous = null;
    item.points.forEach(point => {
      if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) { previous = null; return; }
      path += (!previous ? ' M ' : step ? ' H ' + x(point.x) + ' V ' : ' L ') + (step && previous ? y(point.y) : x(point.x) + ' ' + y(point.y));
      previous = point;
    });
    svg += '<path fill="none" stroke="' + item.color + '" stroke-width="2.5" d="' + path + '"/>';
  });
  markers.forEach((marker, index) => {
    svg += '<line x1="' + x(marker.x) + '" x2="' + x(marker.x) + '" y1="30" y2="235" stroke="#ffad32" stroke-width="1.5" stroke-dasharray="5 4"/><text x="' + x(marker.x) + '" y="' + (-28 + index % 3 * 13) + '" text-anchor="middle" style="fill:#ffad32;font-size:10px">' + E(marker.label) + '</text>';
  });
  return '<div class="chart">' + svg + '<text x="445" y="280" text-anchor="middle">대전 시작 후 시간 (초)</text></g></svg></div><div class="legend">' + valid.map(item => '<span><i class="dot" style="background:' + item.color + '"></i>' + E(item.label) + '</span>').join('') + '</div>' + (markers.length ? '<div class="legend">' + markers.map(marker => '<span>│ ' + E(marker.label) + ' · ' + F(marker.x, 3) + '초</span>').join('') + '</div>' : '');
}

/** 상점 방문의 기록 시간을 라운드별 막대와 추세선으로 표시하고 정상 방문 사이의 증감을 반환한다. */
function ShopDurationChart(shops) {
  if (!shops.length) return Empty('이 범위의 상점 방문 기록 없음');
  const width = Math.max(700, shops.length * 170 + 90), height = 345;
  const left = 65, right = width - 25, top = 65, bottom = 245;
  const maximum = Math.max(1, ...shops.map(shop => Number.isFinite(shop.duration) ? shop.duration : 0)) * 1.2;
  const spacing = (right - left) / shops.length;
  const x = index => left + spacing * (index + .5);
  const y = seconds => bottom - seconds / maximum * (bottom - top);
  const groups = [...new Set(shops.map(shop => JSON.stringify([shop.session, shop.tournament])))];
  let svg = '<svg viewBox="0 0 ' + width + ' ' + height + '" style="--chart-width:' + width + 'px;min-width:' + width + 'px" role="img" aria-label="라운드별 상점 체류 시간과 이전 방문 대비 증감"><text x="12" y="20">상점 체류 시간 (초)</text>';
  for (let tick = 0; tick <= 4; tick++) {
    const value = maximum * tick / 4;
    svg += '<line class="grid" x1="' + left + '" x2="' + right + '" y1="' + y(value) + '" y2="' + y(value) + '"/><text x="54" text-anchor="end" y="' + (y(value) + 4) + '">' + F(value, 1) + '</text>';
  }
  shops.forEach((shop, index) => {
    const known = Number.isFinite(shop.duration), previous = shops[index - 1];
    const sameRun = previous && previous.session === shop.session && previous.tournament === shop.tournament;
    const comparable = known && shop.complete && sameRun && previous.complete && Number.isFinite(previous.duration);
    const color = shop.complete ? '#b9ec82' : '#f4c88a';
    if (comparable) svg += '<line x1="' + x(index - 1) + '" x2="' + x(index) + '" y1="' + y(previous.duration) + '" y2="' + y(shop.duration) + '" stroke="#b9ec82" stroke-width="2"/>';
    if (known) {
      const barWidth = Math.min(62, spacing * .42);
      svg += '<rect x="' + (x(index) - barWidth / 2) + '" y="' + y(shop.duration) + '" width="' + barWidth + '" height="' + (bottom - y(shop.duration)) + '" fill="' + color + '" fill-opacity=".2" stroke="' + color + '"' + (shop.complete ? '' : ' stroke-dasharray="4 3"') + '/><circle cx="' + x(index) + '" cy="' + y(shop.duration) + '" r="4" fill="' + color + '"/>';
    }
    const group = JSON.stringify([shop.session, shop.tournament]);
    const label = (groups.length > 1 ? 'T' + (groups.indexOf(group) + 1) + ' · ' : '') + F(shop.round) + 'R 이후';
    const delta = comparable ? '이전 대비 ' + (shop.duration - previous.duration > 0 ? '+' : '') + F(shop.duration - previous.duration, 3) + '초' : shop.complete ? '비교 기준 없음' : '미완료 · 증감 비교 제외';
    svg += '<text x="' + x(index) + '" y="' + (known ? y(shop.duration) - 13 : bottom - 12) + '" text-anchor="middle" style="fill:' + color + ';font-weight:600">' + (known ? F(shop.duration, 3) + '초' : '시간 미수집') + '</text><text x="' + x(index) + '" y="270" text-anchor="middle">' + label + '</text><text x="' + x(index) + '" y="291" text-anchor="middle">' + (shop.complete ? '완료' : '미완료') + ' · 방문 ' + (index + 1) + '</text><text x="' + x(index) + '" y="313" text-anchor="middle">' + delta + '</text>';
  });
  return '<div class="chart">' + svg + '</svg></div><p class="hint">실선: 정상 완료 방문의 추세 · 점선 막대: 미완료 방문. 증감은 같은 실행의 연속된 완료 방문끼리만 비교합니다. 종료 시간이 없으면 막대를 그리지 않습니다.</p>';
}
/** 상단 파일·토너먼트·대전 선택지를 갱신하고 현재 선택 범위를 유지한다. */
function SyncSelectors() {
  const file = Dataset();
  const fill = (id, options, value) => { const select = document.getElementById(id); select.replaceChildren(...options.map(option => new Option(option[1], option[0]))); select.value = value; select.disabled = !options.length; };
  fill('fileSelect', state.files.map(item => [item.id, item.name]), state.file);
  const tournaments = file?.tournaments.filter(item => item.runs.length) || [];
  if (!tournaments.some(item => item.key === state.tournament)) state.tournament = 'ALL';
  fill('tournamentSelect', file ? [['ALL', '전체 세션 · 토너먼트'], ...tournaments.map((item, index) => [item.key, 'T' + (index + 1) + ' · 세션 ' + (item.session?.slice(0, 8) || '미수집') + ' / ' + (item.id?.slice(0, 8) || '토너먼트 ID 미수집')])] : [], state.tournament);
  const runs = Runs();
  if (!runs.some(run => run.key === state.battle)) state.battle = runs[0]?.key || '';
  fill('battleSelect', runs.map((run, index) => [run.key, (index + 1) + '. ' + RunLabel(run) + ' · ' + run.outcome]), state.battle);
  const index = runs.findIndex(run => run.key === state.battle);
  document.getElementById('prevBattle').disabled = index <= 0;
  document.getElementById('nextBattle').disabled = index < 0 || index >= runs.length - 1;
  document.getElementById('removeFile').disabled = !file;
}

/** 선택 실행의 전체 진행과 대전 성과, 입력 및 데이터 진단 화면을 반환한다. */
function Overview() {
  const file = Dataset(), runs = Runs(), rows = Rows(), input = Playtest.Inputs(rows, file, state.scope);
  const stageKeys = new Set(runs.map(run => run.key));
  const shops = file.shops.filter(shop => state.tournament === 'ALL' || JSON.stringify([shop.session, shop.tournament]) === state.tournament);
  const boards = file.keyboards.filter(board => stageKeys.has(board.run) && board.owner === 'Player' && board.spawn);
  const last = runs.at(-1);
  const finished = rows.some(row => row.record_type === 'InventorySnapshot' && row.action === 'BeforeTournamentReset' && row.phase === 'Finished');
  const disposition = runs.some(run => !run.complete) ? '미완료 대전 포함' : finished ? '종료 관측 · ' + (last?.outcome || '승패 미수집') : last?.outcome === '패배' ? '마지막 대전 패배' : last?.outcome === '승리' ? '기록된 마지막 대전 승리' : '진행 결과 미확정';
  let html = '<div class="metrics">' + Metric('관측된 진행', E(disposition), '최종 순위와 토너먼트 완주를 추정하지 않음') + Metric('대전 완료 / 전체', runs.filter(run => run.complete).length + ' / ' + runs.length, 'StageEnd.is_complete 기준') + Metric('플레이 가능 Down', F(Playtest.Inputs(rows, file).total), 'Alive + Broken · Up 제외') + Metric('플레이어 최초 / 보충', file.header.includes('keyboard_id') ? boards.filter(board => board.spawn.spawn_kind === 'Initial').length + ' / ' + boards.filter(board => board.spawn.spawn_kind === 'Replacement').length : '미수집', '입장 완료 이벤트 기준') + '</div>';
  html += Panel('대전별 결과', Table(['대전', '상태', '플레이어 / 로봇', '기록 길이', '플레이 가능 / 원본 Down', '종료 사유', '탐색'], runs.map(run => [E(RunLabel(run)), Badge(run), Field(run.end, 'player_score', 0) + ' / ' + Field(run.end, 'enemy_score', 0) + ' 점', Field(run.end, 'duration_seconds') + ' 초', F(run.inputs.total) + ' / ' + F(Playtest.Inputs(run.rows, file, 'all').total), Field(run.end, 'end_reason'), '<button data-run="' + E(run.key) + '" data-target="battle">대전 보기</button>']), 'rounds'));
  const max = Math.max(1, ...runs.map(run => Playtest.Inputs(run.rows, file, 'all').raw));
  html += Panel('대전별 입력 구성', runs.map(run => InputStack(RunLabel(run), Playtest.Inputs(run.rows, file, state.scope), max)).join('') || Empty('대전 구분 미수집'), '파랑: 일반키 / 주황: 기믹키 / 회색: 분류 불가. 같은 입력 횟수 눈금으로 비교합니다.');
  const flow = rows.filter(row => ['SessionStart', 'SessionEnd', 'StageStart', 'StageEnd', 'ShopStart', 'ShopEnd', 'KeyboardSpawn'].includes(row.record_type));
  html += Panel('전체 진행 타임라인', Table(['앱 시각(초)', '기록', '대전', '소유자', '입장 / 종료 상태'], flow.map(row => [Field(row, 'elapsed_seconds'), E(row.record_type), F(row.round_index) + 'R · ' + F(row.battle_kind), F(row.owner), row.record_type === 'KeyboardSpawn' ? '키보드 ' + F(row.keyboard_spawn_index) + ' · ' + F(row.spawn_kind) : F(row.end_reason) + (row.record_type.endsWith('End') ? ' · ' + (row.is_complete === true ? '완료' : '미완료') : '')]), 'flow'), '시각은 앱 실행 실시간입니다. 같은 시각의 이벤트는 숫자 sequence 순으로 정렬합니다.');
  html += Panel('라운드별 상점 체류 시간', file.header.includes('shop_visit_id') ? ShopDurationChart(shops) : Empty('상점 방문 미수집'), '폐기 화면·교체 준비까지 포함한 기록 시간입니다. 대전이 끝난 뒤 방문한 상점을 표시합니다.');
  html += Panel('키별 원본 입력 · 배치 범위 밖도 포함', Table(['원본 키 식별자', '전체 Down', '일반 / 기믹 / 분류 불가'], [...Playtest.Group(rows.filter(Playtest.IsDown), row => row.key)].map(([key, entries]) => {
    const count = Playtest.Inputs(entries, file, 'all');
    return [F(key), entries.length, F(count.normal) + ' / ' + F(count.special) + ' / ' + F(count.unknown)];
  }), 'rawKeys'), '대전 밖·비활성·배치 없는 키를 포함합니다. Enter와 NumpadEnter 등 원본 키 식별자를 합치지 않습니다.');
  html += Panel('데이터 진단 · 원본 기록', '<p class="hint">schema ' + E(file.version) + ' · ' + file.header.length + '열 · ' + file.rows.length.toLocaleString() + '행. 원본 문자열과 행별 값을 별도로 보존합니다.</p>' + (file.warnings.length ? '<div class="warning">' + file.warnings.slice(0, 20).map(E).join('<br>') + (file.warnings.length > 20 ? '<br>추가 경고 ' + (file.warnings.length - 20) + '건' : '') + '</div>' : '<p class="hint">파싱 경고 없음</p>') + '<details><summary>원본 이벤트 확인 (페이지당 35행)</summary>' + Table(['원본 레코드', 'sequence', '앱 시각', '종류', 'key / action', '상태', '원본 JSON'], rows.map(row => () => [F(row.sourceIndex), Field(row, 'sequence', 0), Field(row, 'elapsed_seconds', 6), F(row.record_type), F(row.key || row.action), F(row.input_state), '<details><summary>원본</summary><pre>' + E(JSON.stringify(row.raw, null, 2)) + '</pre></details>']), 'raw') + '</details>');
  return html;
}

/** 선택 대전의 점수·입력·콤보와 실제 입장·기믹 이벤트를 표시하는 화면을 반환한다. */
function BattleView() {
  const file = Dataset(), run = Battle(); if (!run) return Empty('대전 식별 기록이 없습니다. 실행 개요의 원본 입력을 확인하세요.');
  const input = Playtest.Inputs(run.rows, file, state.scope), all = Playtest.Inputs(run.rows, file, 'all');
  const denominator = input.total;
  let html = '<div class="metrics">' + Metric(RunLabel(run), Badge(run), run.id) + Metric('플레이어 / 로봇 최종 점수', Field(run.end, 'player_score', 0) + ' / ' + Field(run.end, 'enemy_score', 0), 'StageEnd · 단위: 점') + Metric('선택 범위 Down', F(input.total), '원본 전체 Down ' + F(all.total)) + Metric('파괴 위치 재입력률', input.broken !== null && denominator > 0 ? F(input.broken / denominator * 100, 1) + '%' : input.broken === 0 && denominator === 0 ? '해당 없음' : '미수집', '선택 범위 true Down ' + F(input.broken) + ' / Down ' + F(denominator)) + '</div>';
  html += '<p class="hint">대전 ' + E(run.id) + ' · 종료 사유 ' + Field(run.end, 'end_reason') + ' · 재입력 원본 ' + F(all.broken) + ' / StageEnd 집계 ' + Field(run.end, 'broken_down_count', 0) + '</p>';
  const start = run.start?.elapsed_seconds;
  const scores = Playtest.Scores(run);
  const scorePoints = scores.player, enemyPoints = scores.enemy;
  html += Panel('기록된 점수 추이', Plot([{ label: '플레이어 기록 점수', color: '#b9ec82', points: scorePoints }, { label: '로봇 기록 점수', color: '#b5a0f2', points: enemyPoints }], '점', true) + (enemyPoints.length ? '' : '<p class="hint">로봇 ScoreSnapshot은 미수집입니다. 최종 점수로 추이를 만들지 않습니다.</p>'));
  const buckets = Playtest.Buckets(run, file, state.scope, state.interval);
  html += Panel('시간 구간별 Down', '<label class="hint">집계 간격 <select id="intervalSelect">' + [1, 2, 5, 10].map(value => '<option ' + (state.interval === value ? 'selected' : '') + '>' + value + '</option>').join('') + '</select> 초</label>' + Plot([{ label: '선택 범위 Down', color: '#65bed4', points: buckets.flatMap(bucket => [{ x: bucket.start, y: bucket.count }, { x: bucket.end, y: bucket.count }]) }], 'Down 횟수', true, Playtest.ReplacementMarkers(run)) + Table(['구간(초)', 'Down'], buckets.map(bucket => [F(bucket.start, 2) + '–' + F(bucket.end, 2), F(bucket.count)]), 'buckets'), '횟수 분포입니다. 주황 세로 점선은 플레이어 키보드 보충 후 등장 연출이 끝나 실제 입력 가능한 시점입니다. 최초 입장과 로봇 입장은 표시하지 않습니다. 기록 APM과는 별도 지표이며 마지막 구간은 짧을 수 있습니다.');
  html += Panel('입력 범위와 상태', InputStack('현재 범위', input, Math.max(1, all.raw)) + Table(['원본 input_state', 'Down'], [...all.states].map(([status, entries]) => [E(status), entries.length]), 'states'), '기본은 Alive·Broken. 비활성 포함은 InputDisabled 추가. 원본 전체는 모든 상태를 포함하며 식별할 수 없는 입력은 분류 불가입니다.');
  const intervals = scores.intervals, apm = scores.apm;
  html += Panel('콤보 경계 · 기록 APM', Plot([{ label: 'IntervalSummary.apm', color: '#b5a0f2', points: apm }], '기록 APM', true) + Table(['시작 / 끝(상대 초)', '콤보', '구간', 'APM', '점수/초', '마감 사유'], intervals.map(row => [Number.isFinite(start) ? F(row.start_elapsed_seconds === null ? null : row.start_elapsed_seconds - start, 3) + ' / ' + F(row.end_elapsed_seconds === null ? null : row.end_elapsed_seconds - start, 3) : '미수집', Field(row, 'combo', 0), F(row.interval_kind), Field(row, 'apm', 2), Field(row, 'score_per_second', 2), F(row.end_reason)]), 'intervals'), 'APM은 CSV 값을 그대로 사용합니다. 포함 조건은 시간별 Down 분포와 다를 수 있습니다.');
  const markers = run.rows.filter(row => ['ComboBoundary', 'KeyboardSpawn', 'SpecialKeycapEvent'].includes(row.record_type));
  html += Panel('대전 이벤트', Table(['상대 시간(초)', 'sequence', '종류', '소유자', '키보드', '키 / 유형', '상태 / 사유'], markers.map(row => [Number.isFinite(start) && Number.isFinite(row.elapsed_seconds) ? F(row.elapsed_seconds - start, 3) : '미수집', Field(row, 'sequence', 0), E(row.record_type), F(row.owner), F(row.keyboard_spawn_index), F(row.key) + ' / ' + F(row.special_type), F(row.action || row.spawn_kind) + ' · ' + F(row.end_reason)]), 'markers'), '키보드 입장은 실제 사용 가능 시점이며, 효과 발동은 Activated만 뜻합니다.');
  html += '<button class="primary" data-run="' + E(run.key) + '" data-target="keyboard">이 대전의 키보드 분석 →</button>';
  return html;
}

/** 키보드의 선택 범위 입력을 원본 키 식별자별로 세어 Map을 반환한다. */
function KeyCounts(board, file) { return Playtest.Group(Playtest.Inputs(board.rows, file, state.scope).selected, row => row.key); }
/** 실제 키 식별자에 따라 시각적 키 폭을 반환하며 Enter 계열을 합치지 않는다. */
function KeyWidth(key) {
  return ({ Space: 6.25, Backspace: 2, Tab: 1.5, CapsLock: 1.75, Enter: 2.25, LeftShift: 2.25, RightShift: 2.75, ShiftLeft: 2.25, ShiftRight: 2.75, Backslash: 1.5, LeftCtrl: 1.25, RightCtrl: 1.25, LeftAlt: 1.25, RightAlt: 1.25, LeftWindows: 1.25, RightWindows: 1.25, ContextMenu: 1.25 })[key] || 1;
}
/** 로그의 좌표와 키 식별자로 실제 키 배열 및 공통 범위 히트맵을 반환한다. */
function KeyboardGrid(board, file, max) {
  const counts = KeyCounts(board, file), layout = [...board.layout.values()];
  if (!layout.length) return Empty('실제 KeyboardLayout 미수집 · 원본 키 입력은 아래 목록에서 확인할 수 있습니다.');
  const coordinateRows = Playtest.Group(layout.filter(row => Number.isInteger(row.layout_row) && Number.isInteger(row.layout_column)), row => row.layout_row);
  let html = '<div class="keyboard-scroll"><div class="keyboard" style="grid-template-columns:repeat(80,minmax(7px,1fr))">';
  [...coordinateRows].sort((a, b) => a[0] - b[0]).forEach(([rowIndex, keys]) => {
    let cursor = 1;
    keys.sort((a, b) => a.layout_column - b.layout_column).forEach(row => {
      const width = Math.round(KeyWidth(row.key) * 4), count = counts.get(row.key)?.length || 0;
      const start = Math.max(cursor, row.layout_column * 4 + 1);
      const heatColor = board.owner === 'Enemy' || count === 0 ? '#18202c' : 'rgba(239,55,55,' + (.16 + .72 * count / max) + ')';
      html += '<button class="key special-' + (Playtest.TYPES.includes(row.special_type) ? row.special_type : 'unknown') + (state.key === row.key ? ' selected' : '') + '" data-key="' + E(row.key) + '" data-board="' + E(board.key) + '" style="grid-row:' + (rowIndex + 1) + ';grid-column:' + start + '/span ' + width + ';background:' + heatColor + '" aria-label="' + E(row.key + ', ' + (row.special_type || '유형 미수집') + ', ' + (board.owner === 'Enemy' ? '입력 미수집' : count + '회')) + '"><b>' + E(row.key) + '</b><small>' + F(row.special_type) + '</small><span>' + (board.owner === 'Enemy' ? '미수집' : count + '회') + '</span></button>';
      cursor = start + width;
    });
  });
  return html + '</div></div><p class="hint">배경: 타수가 많을수록 진한 빨강 · 0–' + max + ' Down 공통 범위 · 테두리와 유형명: 기믹 배치 · 좌표 미수집 키 ' + (layout.length - [...coordinateRows.values()].flat().length) + '개</p>';
}

/** 선택 대전의 키보드와 키 세부 입력, 원본 누락 키 목록을 반환한다. */
function KeyboardView() {
  const file = Dataset(), run = Battle(); if (!run) return Empty('먼저 대전을 선택하세요.');
  const boards = file.keyboards.filter(board => board.run === run.key && board.owner === state.owner);
  if (!boards.some(board => board.key === state.board)) { state.board = boards[0]?.key || ''; state.key = ''; }
  const board = boards.find(item => item.key === state.board);
  let html = '<div class="controls"><label>소유자 <select id="ownerSelect"><option value="Player" ' + (state.owner === 'Player' ? 'selected' : '') + '>플레이어</option><option value="Enemy" ' + (state.owner === 'Enemy' ? 'selected' : '') + '>로봇</option></select></label>';
  const options = boards.map(item => '<option value="' + E(item.key) + '" ' + (item.key === state.board ? 'selected' : '') + '>키보드 ' + F(item.spawn?.keyboard_spawn_index) + ' · ' + F(item.spawn?.spawn_kind) + '</option>').join('');
  html += '<label>키보드 <select id="boardSelect">' + options + '</select></label><label>비교 <select id="compareBoardSelect"><option value="">비교 안 함</option>' + boards.filter(item => item.key !== state.board).map(item => '<option value="' + E(item.key) + '" ' + (item.key === state.compareBoard ? 'selected' : '') + '>키보드 ' + F(item.spawn?.keyboard_spawn_index) + '</option>').join('') + '</select></label></div>';
  if (!board) return html + Empty('이 소유자의 개별 키보드 입장·식별 기록이 없습니다. 실행 개요의 키별 원본 입력에서 모든 키를 확인할 수 있습니다.');
  const maximum = Math.max(1, ...file.keyboards.flatMap(item => [...KeyCounts(item, file).values()].map(entries => entries.length)));
  const input = Playtest.Inputs(board.rows, file, state.scope), effects = Playtest.Effects(board.rows);
  const next = boards[boards.indexOf(board) + 1];
  const gap = Number.isFinite(next?.spawn?.elapsed_seconds) && Number.isFinite(board.spawn?.elapsed_seconds) ? next.spawn.elapsed_seconds - board.spawn.elapsed_seconds : null;
  html += '<div class="metrics">' + Metric('입장', '키보드 ' + Field(board.spawn, 'keyboard_spawn_index', 0), board.spawn?.spawn_kind || '미수집') + Metric('선택 범위 Down', state.owner === 'Enemy' ? '미수집' : F(input.total), '재입력 ' + (state.owner === 'Enemy' ? '미수집' : F(input.broken))) + Metric('기믹 발동', file.header.includes('effect_id') ? F(effects.reduce((sum, item) => sum + item.Activated, 0)) : '미수집', 'effect_id별 Activated') + Metric('다음 입장까지', gap === null ? '해당 없음' : F(gap, 3) + '초', gap === null ? '마지막 관찰 키보드 · 관찰 시간이 짧을 수 있음' : '교체·등장 연출을 포함하는 입장 간격') + '</div>';
  html += '<p class="hint">ID ' + E(board.id) + ' · 입장 앱 시각 ' + Field(board.spawn, 'elapsed_seconds', 6) + '초. 로봇의 직접 입력량은 미수집입니다. 키보드가 표시하는 일반키 수와 보유 슬롯의 None 수량은 다른 범위입니다.</p>';
  const inputMaximum = Math.max(1, ...file.keyboards.map(item => Playtest.Inputs(item.rows, file, 'all').raw));
  html += Panel('이 키보드의 입력 구성', state.owner === 'Enemy' ? Empty('로봇 직접 입력은 미수집입니다.') : InputStack('선택 범위', input, inputMaximum), '같은 파일의 모든 키보드가 공통 입력 횟수 눈금을 사용합니다.');
  html += Panel('이 키보드의 기믹 상태', file.header.includes('effect_id') ? Table(['유형', '파괴', '발동', '발동 전 취소', '대기 미종결'], effects.map(item => [E(item.type), item.Broken, item.Activated, item.Cancelled, item.pending]), 'boardEffects') : Empty('기믹 이벤트 미수집'));
  html += Panel('실제 키 배열 · 키를 선택해 상세 보기', KeyboardGrid(board, file, maximum));
  const compare = boards.find(item => item.key === state.compareBoard && item.key !== board.key);
  if (compare) html += Panel('같은 눈금 비교 · 키보드 ' + F(compare.spawn?.keyboard_spawn_index), KeyboardGrid(compare, file, maximum));
  if (state.key) {
    const layout = board.layout.get(state.key), entries = board.rows.filter(row => row.key === state.key);
    const counts = Playtest.Inputs(entries, file, state.scope), keyEffects = Playtest.Effects(entries);
    html += '<section class="key-details"><h3>' + E(state.key) + ' · ' + F(layout?.special_type) + '</h3><p>선택 범위 Down ' + (state.owner === 'Enemy' ? '미수집' : F(counts.total)) + ' · 원본 Down ' + (state.owner === 'Enemy' ? '미수집' : F(counts.raw)) + ' · 파괴 위치 재입력 ' + (state.owner === 'Enemy' ? '미수집' : F(counts.broken)) + '</p>' + (file.header.includes('effect_id') ? Table(['유형', '파괴', '발동', '발동 전 취소'], keyEffects.map(item => [E(item.type), item.Broken, item.Activated, item.Cancelled]), 'keyEffects') : Empty('기믹 이벤트 미수집')) + '</section>';
  }
  const actualKeys = new Set([...board.layout.keys(), ...board.rows.filter(Playtest.IsDown).map(row => row.key)]);
  html += Panel('키별 수치 · 배치 없는 원본 키도 포함', Table(['키', '실제 유형', '선택 Down', '원본 Down', '재입력', '탐색'], [...actualKeys].map(key => {
    const counts = Playtest.Inputs(board.rows.filter(row => row.key === key), file, state.scope);
    return [E(key), F(board.layout.get(key)?.special_type), state.owner === 'Enemy' ? '미수집' : F(counts.total), state.owner === 'Enemy' ? '미수집' : F(counts.raw), state.owner === 'Enemy' ? '미수집' : F(counts.broken), '<button data-key="' + E(key) + '" data-board="' + E(board.key) + '">상세</button>'];
  }), 'keys'));
  const unassigned = Playtest.Inputs(run.rows, file, 'all').selected.filter(row => !file.layouts.get(row.session_id + ':' + row.keyboard_id)?.has(row.key));
  html += Panel('대전 내 배치에 연결되지 않은 원본 입력', Table(['키', '상태', 'Down'], [...Playtest.Group(unassigned, row => JSON.stringify([row.key, row.input_state]))].map(([, entries]) => [F(entries[0].key), F(entries[0].input_state), entries.length]), 'unassigned'), '키보드 미식별·배치 미수집 입력을 누락하거나 임의로 일반키에 배분하지 않습니다.');
  return html;
}

/** 토너먼트 범위의 보유 변화와 효과 상태, 상점 방문·상품·거래 흐름을 반환한다. */
function ShopView() {
  const file = Dataset(), rows = Rows();
  const selectedRows = new Set(rows);
  const snapshots = file.snapshots.filter(snapshot => selectedRows.has(snapshot.context));
  const shops = file.shops.filter(shop => state.tournament === 'ALL' || JSON.stringify([shop.session, shop.tournament]) === state.tournament);
  let html = Panel('기믹 상태의 의미', '<p class="hint">파괴는 Broken, 발동은 Activated만 집계합니다. Queued는 대기이며, KeyboardDestroyed 취소는 사용자 취소가 아닙니다. Activated도 피해·득점·효과 완료를 보장하지 않습니다.</p>');
  html += '<div class="grid2">' + ['Player', 'Enemy'].map(owner => Panel(owner === 'Player' ? '플레이어 기믹' : '로봇 기믹', file.header.includes('effect_id') ? Table(['유형', '파괴', '발동', '발동 전 취소', '생략', '대기 미종결'], Playtest.Effects(rows, owner).map(item => [E(item.type), item.Broken, item.Activated, item.Cancelled, item.Skipped, item.pending]), 'effect-' + owner) : Empty('기믹 이벤트 미수집'))).join('') + '</div><br>';
  const capacities = snapshots.map(snapshot => snapshot.context.inventory_capacity).filter(Number.isFinite);
  const maxCapacity = Math.max(1, ...capacities);
  html += Panel('양측 보유 구성 변화', !file.header.includes('snapshot_id') ? Empty('보유 구성 미수집') :
    Table(['시각', '대전 / 소유자', '계기', '보유 구성', '용량 / 특수 합계', '재화', 'snapshot_id'], snapshots.map(snapshot => [Field(snapshot.context, 'elapsed_seconds'), F(snapshot.context.round_index) + 'R / ' + F(snapshot.context.owner), F(snapshot.context.action),
      '<div class="stack">' + Playtest.TYPES.map(type => Number.isFinite(snapshot.quantities[type]) ? '<span style="width:' + snapshot.quantities[type] / maxCapacity * 100 + '%;background:' + TYPE_COLORS[type] + '"></span>' : '').join('') + '</div>' + Playtest.TYPES.map(type => E(type) + ' ' + F(snapshot.quantities[type])).join(' · '), Field(snapshot.context, 'inventory_capacity', 0) + ' / ' + Field(snapshot.context, 'inventory_total', 0), Field(snapshot.context, 'currency_after', 0), F(snapshot.context.snapshot_id)]), 'inventory'), '같은 보유 용량 눈금입니다. None은 보유 슬롯의 기본 수량이며 물리 키보드의 일반키 개수가 아닙니다. BattleStart는 stage_run_id에 연결됩니다.');
  if (!file.header.includes('shop_visit_id')) return html + Panel('상점', Empty('상점 방문·거래 미수집'));
  const actions = shops.flatMap(shop => shop.actions);
  const purchases = actions.filter(row => row.action === 'PurchaseConfirmed');
  const rerolls = actions.filter(row => row.action === 'Reroll');
  const collected = shops.map(shop => shop.duration).filter(Number.isFinite);
  html += '<div class="metrics">' + Metric('종료 기록 상점 시간', !shops.length ? '0초' : collected.length ? F(collected.reduce((sum, value) => sum + value, 0), 6) + '초' : '미수집', '방문 ' + shops.length + '회 · 미완료 ' + shops.filter(shop => !shop.complete).length + '회') + Metric('실제 구매', file.header.includes('action') ? purchases.length + '회' : '미수집', 'PurchaseConfirmed만 집계') + Metric('구매 지출 / 리롤 지출', file.header.includes('currency_before') ? F(Playtest.Spend(purchases)) + ' / ' + F(Playtest.Spend(rerolls)) : '미수집', '재화 전후 차이 · 묶음 가격') + '</div>';
  html += Panel('라운드별 상점 체류 시간 · 증감 추세', ShopDurationChart(shops), '각 막대는 한 번의 상점 방문입니다. 위 숫자는 체류 시간, 아래 숫자는 이전 방문 대비 증감입니다.');
  if (!shops.length) html += Empty('이 범위의 상점 방문 기록 없음');
  shops.forEach((shop, shopIndex) => {
    const transactionRows = [...shop.transactions].map(([id, entries]) => {
      const transaction = Playtest.Transaction(entries);
      const { quantity, discarded, snapshotId } = transaction;
      const snapshot = snapshots.find(item => item.context.snapshot_id === snapshotId && item.context.owner === 'Player');
      return [E(id), E(transaction.outcome), entries.map(row => F(row.action)).join(' → '), F(quantity), F(discarded), F(transaction.spend), snapshot ? Playtest.TYPES.map(type => type + ' ' + F(snapshot.quantities[type])).join(' · ') : snapshotId ? '스냅샷 상세 누락' : '해당 없음'];
    });
    const offers = [...shop.offers].map(([, entries]) => {
      const row = entries[0];
      const purchasesForOffer = shop.actions.filter(action => action.action === 'PurchaseConfirmed' && action.offer_slot === row.offer_slot && action.offer_generation === row.offer_generation);
      return [Field(row, 'offer_generation', 0), Field(row, 'offer_slot', 0), F(row.special_type), Field(row, 'quantity', 0), Field(row, 'price', 0), purchasesForOffer.length ? '구매 확정' : '확정 구매 없음', entries.length > 1 ? '중복 제시 기록 ' + entries.length + '행' : '제시 1행'];
    });
    html += Panel(F(shop.round) + 'R 이후 상점 · ' + (shop.complete ? '완료' : '미완료'),
      '<p class="hint">' + E(shop.id) + ' · 체류 ' + Field(shop.end, 'duration_seconds', 6) + '초 · 종료 ' + Field(shop.end, 'end_reason') + '</p>' +
      '<h3>슬롯별 실제 제시 상품</h3>' + Table(['세대', '슬롯(0 기준)', '유형', '묶음 수량', '묶음 가격', '구매 상태', '원본 제시'], offers, 'offers-' + shopIndex) +
      '<h3>거래별 선택 → 확정·취소</h3>' + Table(['거래 ID', '결과', '흐름', '확정 구매 수량', '확정 폐기 수량', '지출', '확정 후 보유'], transactionRows, 'transactions-' + shopIndex) +
      '<h3>시간순 행동 · 리롤은 결과 세대</h3>' + Table(['앱 시각 / sequence', '행동', '세대 / 슬롯', '유형', '수량', '가격', '재화 전 / 후', '확정 여부'], shop.actions.map(row => [Field(row, 'elapsed_seconds') + ' / ' + Field(row, 'sequence', 0), F(row.action), Field(row, 'offer_generation', 0) + ' / ' + (row.offer_slot === null ? '해당 없음' : Field(row, 'offer_slot', 0)), F(row.special_type), Field(row, 'quantity', 0), Field(row, 'price', 0), Field(row, 'currency_before', 0) + ' / ' + Field(row, 'currency_after', 0), row.is_confirmed === true ? '확정' : row.is_confirmed === false ? '제시·후보·취소' : '미수집']), 'actions-' + shopIndex),
      '제시 상품은 방문 ID + 세대 + 슬롯으로 구별합니다. 선택을 구매·폐기로 합산하지 않습니다. 종료 행이 없으면 미완료 방문입니다.');
  });
  html += Panel('발동 전 취소·생략의 원본 사유', Table(['대전', '소유자', '유형', '상태', '사유', 'effect_id'], rows.filter(row => row.record_type === 'SpecialKeycapEvent' && ['Cancelled', 'Skipped'].includes(row.action)).map(row => [F(row.round_index), F(row.owner), F(row.special_type), F(row.action), F(row.end_reason), F(row.effect_id)]), 'cancelled'));
  return html;
}

/** 비교 항목의 짧은 제목과 원본 식별 정보를 표시하고 플레이어별 내용 묶음을 반환한다. */
function ComparisonGroup(item, body) {
  const title = '플레이어 ' + (state.files.indexOf(item.file) + 1) + ' · 실행 ' + (item.index + 1);
  return '<div class="comparison-group"><div class="comparison-group-heading"><h3>' + E(title) + '</h3><details><summary>원본 파일 · ID</summary><p>' + E(item.file.name) + '<br>세션: ' + E(item.tournament.session || '미수집') + '<br>토너먼트: ' + E(item.tournament.id || '미수집') + '</p></details></div>' + body + '</div>';
}

/** 파일별 토너먼트의 완료 조건과 실제 시간으로 비교 화면의 집계와 공통 막대를 반환한다. */
function CompareView() {
  const entries = state.files.flatMap(file => file.tournaments.filter(tournament => tournament.runs.length).map((tournament, index) => ({ file, tournament, index })));
  if (!entries.length) return Empty('비교할 실행이 없습니다. CSV를 불러오세요.');
  let html = '<p class="hint">불러온 모든 파일의 토너먼트를 비교합니다. 플레이어 번호는 불러온 파일을 구분하는 임시 이름입니다. 완료 대전만 기본 집계하며, 플레이 시간은 기록된 duration_seconds 합계입니다.</p><label class="hint"><input id="includeIncomplete" type="checkbox" ' + (state.compareIncomplete ? 'checked' : '') + '> 미완료 대전도 별도 상태를 표시하며 포함</label>';
  const comparisons = entries.map(({ file, tournament, index }) => {
    return { file, tournament, index, ...Playtest.Comparison(file, tournament, state.scope, state.compareIncomplete) };
  });
  const max = Math.max(1, ...comparisons.map(item => item.input.raw));
  html += Panel('입력량 공통 눈금 비교', comparisons.map(item => ComparisonGroup(item, InputStack('선택 범위 입력', item.input, max))).join(''));
  html += Panel('실행별 비교', comparisons.map(item => ComparisonGroup(item, Table(['포함 대전 / 미완료', '선택 Down', '기록 시간', '분당 Down', '재입력률', '상점 시간', '마지막 기록 보유'], [[item.runs.length + ' / ' + item.tournament.runs.filter(run => !run.complete).length,
    F(item.input.total), F(item.duration, 3) + ' 초', F(item.perMinute, 2), item.input.total > 0 && item.input.broken !== null ? F(item.input.broken / item.input.total * 100, 1) + '%' : item.input.total === 0 ? '해당 없음' : '미수집', F(item.shopTime, 6) + ' 초', item.final ? Playtest.TYPES.map(type => type + ' ' + F(item.final.quantities[type])).join(' · ') : '미수집']], 'comparisons-' + item.file.id + '-' + item.index))).join(''), '분당 Down = 선택 범위 Down × 60 / 기록된 대전 시간. CSV의 구간 APM과 별개의 지표입니다. 상점 종료 시간이 누락되면 총시간을 추정하지 않습니다.');
  html += Panel('대전별 성과', comparisons.map(item => ComparisonGroup(item, Table(['대전', '완료·승패', '플레이어 / 로봇 점수', '기록 길이(초)', '선택 Down'], item.runs.map(run => [E(RunLabel(run)), Badge(run), Field(run.end, 'player_score', 0) + ' / ' + Field(run.end, 'enemy_score', 0), Field(run.end, 'duration_seconds'), F(Playtest.Inputs(run.rows, item.file, state.scope).total)]), 'compareRounds-' + item.file.id + '-' + item.index))).join(''), '같은 R이라도 정규전과 연장전은 별도 대전입니다. 미완료의 점수는 중단 시점 기록이며 정상 완료 점수와 동등한 성과로 해석하지 않습니다.');
  return html;
}

/** 현재 탐색 상태에 맞춰 화면을 갱신하며 입력 범위와 선택한 탭을 유지한다. */
function Render() {
  RenderModeShell();
  if (state.mode === "customization") { CustomUI.Render(); return; }
  SyncSelectors();
  const title = titles[state.tab];
  document.getElementById('pageTitle').textContent = title[0];
  document.getElementById('pageSubtitle').textContent = title[1];
  document.getElementById('breadcrumb').textContent = 'WORKSPACE / ' + state.tab.toUpperCase();
  document.querySelectorAll('[data-tab]').forEach(button => { button.classList.toggle('active', button.dataset.tab === state.tab); button.setAttribute('aria-pressed', String(button.dataset.tab === state.tab)); });
  const hasFiles = state.files.length > 0;
  document.getElementById('welcome').hidden = hasFiles;
  const host = document.getElementById('content');
  if (!hasFiles) { host.replaceChildren(); return; }
  const scope = '<div class="controls"><label>입력 집계 범위 <select id="scopeSelect"><option value="playable" ' + (state.scope === 'playable' ? 'selected' : '') + '>플레이 가능 · Alive / Broken</option><option value="mapped" ' + (state.scope === 'mapped' ? 'selected' : '') + '>비활성 포함 · InputDisabled 추가</option><option value="all" ' + (state.scope === 'all' ? 'selected' : '') + '>원본 전체 Down · 모든 상태</option></select></label><span class="hint">Up 제외 · 분류는 keyboard_id + 원본 key로 연결</span></div>';
  host.innerHTML = scope + ({ overview: Overview, battle: BattleView, keyboard: KeyboardView, shop: ShopView, compare: CompareView })[state.tab]();
}

/** 사용자가 고른 로컬 CSV를 개별 파싱하고 파일별 오류와 성공 상태를 화면에 반영한다. */
async function LoadFiles(files) {
  const status = document.getElementById('loadStatus'), errors = [];
  const button = document.getElementById('addFiles'); button.disabled = true;
  for (const file of files) {
    status.textContent = file.name + ' 읽는 중…';
    await new Promise(resolve => requestAnimationFrame(resolve));
    try {
      if (!file.name.toLowerCase().endsWith('.csv')) throw new Error('CSV 파일을 선택하세요.');
      const text = await file.text();
      const id = file.name + ':' + file.size + ':' + file.lastModified;
      const header = (Playtest.ParseCsv(text)[0] || []).map(field => field.trim().toLowerCase());
      if (header.includes('customization_run_id')) {
        CustomUI.Add(Customization.Build(text, file.name, id));
        state.mode = 'customization';
        continue;
      }
      const model = Playtest.Build(text, file.name, id);
      state.mode = 'playtest';
      const existing = state.files.findIndex(item => item.id === id);
      if (existing >= 0) state.files[existing] = model; else state.files.push(model);
      state.file = id;
    } catch (error) { errors.push(file.name + ': ' + error.message); }
  }
  button.disabled = false; state.tournament = 'ALL'; state.battle = ''; state.board = ''; state.key = ''; state.pages = {};
  status.textContent = '플레이테스트 ' + state.files.length + '개 / 커스터마이징 ' + CustomUI.Count() + '개 · 새로고침 시 파일을 다시 선택하세요.';
  document.getElementById('fileErrors').innerHTML = errors.length ? '<div class="warning error">' + errors.map(E).join('<br>') + '</div>' : '';
  document.getElementById('fileInput').value = '';
  Render();
}

/** 사용자 클릭·선택·파일 드롭을 상태 변경과 연결하고 초기 빈 화면을 표시한다. */
function Initialize() {
  const picker = document.getElementById('fileInput');
  document.getElementById('addFiles').addEventListener('click', () => picker.click());
  document.getElementById('welcomeUpload').addEventListener('click', () => picker.click());
  picker.addEventListener('change', event => LoadFiles([...event.target.files]));
  document.getElementById('fileSelect').addEventListener('change', event => { state.file = event.target.value; state.tournament = 'ALL'; state.battle = ''; state.board = ''; state.key = ''; state.pages = {}; Render(); });
  document.getElementById('tournamentSelect').addEventListener('change', event => { state.tournament = event.target.value; state.battle = ''; state.board = ''; state.key = ''; state.pages = {}; Render(); });
  document.getElementById('battleSelect').addEventListener('change', event => { state.battle = event.target.value; state.board = ''; state.key = ''; state.pages = {}; Render(); });
  const moveBattle = direction => { const runs = Runs(), index = runs.findIndex(run => run.key === state.battle); if (runs[index + direction]) { state.battle = runs[index + direction].key; state.board = ''; state.key = ''; state.pages = {}; Render(); } };
  document.getElementById('prevBattle').addEventListener('click', () => moveBattle(-1));
  document.getElementById('nextBattle').addEventListener('click', () => moveBattle(1));
  document.getElementById('removeFile').addEventListener('click', () => {
    if (state.mode === 'customization') CustomUI.Remove();
    else { state.files = state.files.filter(file => file.id !== state.file); state.file = state.files[0]?.id || ''; state.tournament = 'ALL'; }
    state.pages = {}; Render();
  });
  ['customFileSelect', 'testerSelect', 'customSessionSelect', 'customRunSelect'].forEach(id => document.getElementById(id).addEventListener('change', event => { CustomUI.Change(event.target); state.pages = {}; Render(); }));
  document.addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.dataset.mode) { state.mode = button.dataset.mode; state.pages = {}; Render(); window.scrollTo({ top: 0, behavior: 'auto' }); return; }
    if (state.mode === 'customization' && CustomUI.Click(button)) { state.pages = {}; Render(); return; }
    if (button.dataset.tab) { state.tab = button.dataset.tab; state.pages = {}; Render(); window.scrollTo({ top: 0, behavior: 'auto' }); }
    if (button.dataset.run) { state.battle = button.dataset.run; state.tab = button.dataset.target; state.board = ''; state.key = ''; state.pages = {}; Render(); }
    if (button.dataset.key) { state.key = button.dataset.key; state.board = button.dataset.board; Render(); document.querySelector('.key-details')?.scrollIntoView({ block: 'nearest' }); }
    if (button.dataset.page) { state.pages[button.dataset.page] = Math.max(0, (state.pages[button.dataset.page] || 0) + Number(button.dataset.delta)); Render(); }
  });
  document.getElementById('content').addEventListener('change', event => {
    const target = event.target;
    if (state.mode === "customization") { CustomUI.Change(target); state.pages = {}; Render(); return; }
    if (target.id === 'scopeSelect') state.scope = target.value;
    if (target.id === 'ownerSelect') { state.owner = target.value; state.board = ''; state.compareBoard = ''; state.key = ''; }
    if (target.id === 'boardSelect') { state.board = target.value; state.key = ''; }
    if (target.id === 'compareBoardSelect') state.compareBoard = target.value;
    if (target.id === 'intervalSelect') state.interval = Number(target.value);
    if (target.id === 'includeIncomplete') state.compareIncomplete = target.checked;
    state.pages = {}; Render();
  });
  const overlay = document.getElementById('dropOverlay'); let depth = 0;
  window.addEventListener('dragenter', event => { if ([...event.dataTransfer.types].includes('Files')) { event.preventDefault(); depth++; overlay.hidden = false; } });
  window.addEventListener('dragover', event => event.preventDefault());
  window.addEventListener('dragleave', () => { depth = Math.max(0, depth - 1); if (!depth) overlay.hidden = true; });
  window.addEventListener('drop', event => { event.preventDefault(); depth = 0; overlay.hidden = true; LoadFiles([...event.dataTransfer.files]); });
  window.addEventListener('blur', () => { depth = 0; overlay.hidden = true; });
  Render();
}

/** 현재 데이터 유형에 맞춰 메뉴·필터·초기 사용법을 전환하고 파일 수를 표시한다. */
function RenderModeShell() {
  const custom = state.mode === 'customization';
  document.getElementById('playtestNav').hidden = custom;
  document.getElementById('customizationNav').hidden = !custom;
  document.getElementById('domainLabel').textContent = custom ? '커스터마이징 분석' : '플레이테스트 분석';
  document.querySelectorAll('[data-domain]').forEach(element => { element.hidden = element.dataset.domain !== state.mode; });
  document.querySelectorAll('[data-mode]').forEach(button => { button.classList.toggle('active', button.dataset.mode === state.mode); button.setAttribute('aria-pressed', String(button.dataset.mode === state.mode)); });
  document.getElementById('welcomeTitle').textContent = custom ? '편집의 흐름에서, 유지한 선택까지.' : '한 번의 실행부터, 하나의 키까지.';
  document.getElementById('welcomeDescription').textContent = custom ? '커스터마이징 CSV를 선택하거나 놓으세요. 플레이어별 접근·체류·요소 선택·이미지 적용과 최종 상태를 확인합니다. 행동만으로 재미를 단정하지 않습니다.' : '플레이테스트 CSV를 선택하거나 놓으세요. 여러 파일의 대전, 키보드, 기믹과 상점 기록을 비교합니다.';
  document.getElementById('welcomeSteps').innerHTML = custom ? '<span><b>01</b> 플레이어·회차 선택</span><span><b>02</b> 항목·요소 탐색</span><span><b>03</b> 종료 직전 유지 상태 비교</span>' : '<span><b>01</b> 실행의 진행 확인</span><span><b>02</b> 대전과 키보드 탐색</span><span><b>03</b> 다른 실행과 비교</span>';
  document.getElementById('welcomeFine').textContent = custom ? '커스터마이징 전용 schema v1 · 25열 / UTF-8 CSV / 외부 전송 없음' : '플레이테스트 29 · 43 · 59열 / UTF-8 CSV / 원본 파일 변경 없음';
  document.getElementById('analysisFooter').textContent = custom ? '선택 횟수 ≠ 최종 선호 ≠ 재미·만족. Final은 저장 완료가 아닙니다. 미기록 상태를 추정하지 않습니다.' : '기록된 사실을 집계합니다. 입력 ≠ 타격 성공 ≠ 기믹 발동. 미수집 데이터를 추정하지 않습니다.';
  document.getElementById('loadStatus').textContent = '플레이테스트 ' + state.files.length + '개 / 커스터마이징 ' + CustomUI.Count() + '개 · 메모리에만 보관';
}

Initialize();
