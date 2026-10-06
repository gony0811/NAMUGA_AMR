import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const SKILL_DIR = "/Users/sean/.codex/plugins/cache/openai-primary-runtime/presentations/26.904.11930/skills/presentations";
const workspaceDir = "/Users/sean/Documents/GitHub/NAMUGA_AMR";
const buildDir = path.join(workspaceDir, ".artifacts/namuga_amr_ppt/build");
const outputDir = path.join(workspaceDir, ".artifacts/namuga_amr_ppt/output");
const FINAL_PPTX = path.join(outputDir, "NAMUGA_AMR_QR_정밀위치_확보방안_v2.pptx");
const RUNTIME_PYTHON = "/Users/sean/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3";

const { finalizePresentation } = await import(pathToFileURL(
  path.join(SKILL_DIR, "container_tools/artifact_tool_utils.mjs")
).href);

await fs.mkdir(buildDir, { recursive: true });
await fs.mkdir(outputDir, { recursive: true });

const W = 1040;
const H = 720;
const C = {
  wine: "#9B1B30",
  wine2: "#B9243E",
  charcoal: "#262626",
  dark: "#404040",
  gray: "#595959",
  mid: "#BFBFBF",
  light: "#F2F2F2",
  pale: "#F8EFF1",
  white: "#FFFFFF",
  green: "#2E7D5B",
  amber: "#C27A15",
  blue: "#315F8C",
};
const FONT = "Apple SD Gothic Neo";
const LATIN = "Arial";

const p = Presentation.create({ slideSize: { width: W, height: H } });

function box(slide, x, y, w, h, fill = C.white, line = C.mid, radius = false) {
  return slide.shapes.add({
    geometry: radius ? "roundRect" : "rect",
    position: { left: x, top: y, width: w, height: h },
    fill,
    line: { fill: line, width: line === "none" ? 0 : 1 },
  });
}

function text(slide, value, x, y, w, h, size = 16, color = C.charcoal, bold = false, align = "left", font = FONT) {
  const s = slide.shapes.add({
    geometry: "textbox",
    position: { left: x, top: y, width: w, height: h },
    fill: "none",
    line: { fill: "none", width: 0 },
  });
  s.text = value;
  s.text.style = {
    typeface: font,
    fontSize: size,
    color,
    bold,
    alignment: align,
    verticalAlignment: "middle",
    autoFit: "shrinkText",
  };
  return s;
}

function line(slide, x, y, w, h, color = C.mid, width = 1) {
  return slide.shapes.add({
    geometry: "line",
    position: { left: x, top: y, width: w, height: h },
    fill: "none",
    line: { fill: color, width },
  });
}

function arrow(slide, x, y, w, h, color = C.wine) {
  return slide.shapes.add({
    geometry: "rightArrow",
    position: { left: x, top: y, width: w, height: h },
    fill: color,
    line: { fill: color, width: 0 },
  });
}

function header(slide, title, page, subtitle = "") {
  slide.background.fill = C.white;
  text(slide, title, 40, 20, 720, 42, 24, C.charcoal, true);
  if (subtitle) text(slide, subtitle, 42, 57, 700, 24, 10.5, C.gray, false);
  text(slide, "Confidential", 860, 14, 140, 16, 8, C.gray, false, "right", LATIN);
  text(slide, "NAMUGA AMR", 820, 30, 180, 23, 13, C.charcoal, true, "right", LATIN);
  text(slide, "Precision Handling System", 820, 50, 180, 15, 8, C.gray, false, "right", LATIN);
  line(slide, 40, 72, 960, 0, C.mid, 1);
  box(slide, 1012, 95, 8, 545, C.wine, "none");
  text(slide, "NAMUGA AMR", 996, 240, 42, 200, 8, C.white, true, "center", LATIN).rotation = 90;
  text(slide, `Copyright © NAMUGA. 2026. All Rights Reserved.`, 40, 690, 470, 16, 7.5, C.mid, false, "left", LATIN);
  text(slide, String(page), 973, 690, 27, 16, 8, C.mid, false, "right", LATIN);
}

function sectionLabel(slide, n, title, x, y, w = 250) {
  box(slide, x, y, 24, 24, C.wine, "none");
  text(slide, String(n).padStart(2, "0"), x, y, 24, 24, 9, C.white, true, "center", LATIN);
  text(slide, title, x + 34, y, w - 34, 24, 13, C.charcoal, true);
}

function note(slide, body) {
  slide.speakerNotes.textFrame.setText(body);
}

// 1. Cover
{
  const s = p.slides.add();
  s.background.fill = C.white;
  text(s, "Confidential", 850, 18, 150, 18, 8, C.gray, false, "right", LATIN);
  box(s, 40, 135, 8, 255, C.wine, "none");
  text(s, "NAMUGA AMR", 76, 156, 650, 42, 17, C.wine, true, "left", LATIN);
  text(s, "제어 구성 및 QR 기반\n매거진 PICKUP / PLACE 위치 정밀도 확보 방안", 76, 205, 830, 108, 30, C.charcoal, true);
  text(s, "AMR 정차 오차 ±50 mm를 비전 보정으로 흡수하는 시스템 설계", 78, 325, 800, 34, 15, C.gray, false);
  text(s, "시스템 개요 · 보정 원리 · 작업 시퀀스 · 검증 기준", 78, 373, 690, 27, 12, C.wine, true);
  line(s, 76, 438, 865, 0, C.mid, 1);
  text(s, "NAMUGA  |  2026. 10", 78, 466, 400, 24, 12, C.charcoal, true, "left", LATIN);
  text(s, "NAMUGA AMR", 790, 585, 220, 34, 22, C.charcoal, true, "right", LATIN);
  text(s, "QR Guided Precision Handling", 790, 620, 220, 22, 10, C.gray, false, "right", LATIN);
  text(s, "Copyright © NAMUGA. 2026. All Rights Reserved.", 40, 690, 470, 16, 7.5, C.mid, false, "left", LATIN);
  note(s, "본 자료는 NAMUGA_AMR 구현 코드와 인터페이스 문서를 기반으로 작성함. 정량 성능은 현장 POC에서 검증 필요.");
}

// 2. Control architecture
{
  const s = p.slides.add();
  header(s, "NAMUGA AMR 제어 구성도", 2, "관제 명령부터 협동로봇 위치 보정까지 단일 시퀀스로 연계");

  const acs = box(s, 65, 105, 190, 76, C.wine, "none");
  text(s, "ACS 관제 서버", 75, 112, 170, 26, 16, C.white, true, "center");
  text(s, "moveCmd · actionCmd\nMQTT", 75, 140, 170, 32, 10, C.white, false, "center", LATIN);

  const pc = box(s, 352, 94, 375, 98, C.dark, "none");
  text(s, "AMR 통합 제어 PC", 370, 105, 339, 30, 18, C.white, true, "center");
  text(s, "Main Sequence · 상태 관리 · 오프셋 계산 · 이력", 370, 140, 339, 30, 11, C.white, false, "center");

  const amr = box(s, 820, 105, 170, 76, C.light, C.mid);
  text(s, "AMR Controller", 832, 113, 146, 24, 14, C.charcoal, true, "center", LATIN);
  text(s, "Node 이동 · 정차\n상태 / 알람", 832, 140, 146, 30, 10, C.gray, false, "center");

  arrow(s, 274, 127, 54, 24, C.wine);
  text(s, "Wi-Fi / MQTT", 265, 103, 73, 18, 9, C.wine, true, "center", LATIN);
  arrow(s, 747, 127, 54, 24, C.gray);
  text(s, "Modbus TCP", 738, 103, 75, 18, 9, C.gray, true, "center", LATIN);

  sectionLabel(s, 1, "통합 제어 PC 내부 서비스", 65, 224, 350);
  const svcY = 270;
  const svc = [
    ["MainSequence", "10단계 작업 상태머신"],
    ["CameraService", "RGB·Depth / QR 좌표"],
    ["Offset Service", "Port + Model 보정"],
    ["CobotService", "DI/DO·AI 레지스터"],
  ];
  svc.forEach((it, i) => {
    const x = 65 + i * 240;
    box(s, x, svcY, 208, 70, i === 0 ? C.pale : C.light, i === 0 ? C.wine : C.mid);
    text(s, it[0], x + 12, svcY + 8, 184, 22, 13, i === 0 ? C.wine : C.charcoal, true, "center", LATIN);
    text(s, it[1], x + 12, svcY + 34, 184, 24, 10, C.gray, false, "center");
    if (i < svc.length - 1) arrow(s, x + 213, svcY + 25, 20, 18, C.mid);
  });

  sectionLabel(s, 2, "현장 장치 및 데이터 흐름", 65, 378, 350);
  const cam = box(s, 65, 424, 220, 104, C.white, C.mid);
  text(s, "RGB-D 카메라", 80, 435, 190, 25, 15, C.charcoal, true, "center");
  text(s, "QR 중심점 · 회전각\nDepth 기반 실제 거리", 80, 466, 190, 42, 11, C.gray, false, "center");
  const cobot = box(s, 430, 424, 220, 104, C.white, C.mid);
  text(s, "협동로봇", 445, 435, 190, 25, 15, C.charcoal, true, "center");
  text(s, "AI0: dx · AI1: dy\nAI2: dθ ×100", 445, 466, 190, 42, 11, C.gray, false, "center", LATIN);
  const io = box(s, 795, 424, 195, 104, C.white, C.mid);
  text(s, "I/O · 안전 장치", 808, 435, 169, 25, 15, C.charcoal, true, "center");
  text(s, "Busy · Complete · Error\n인터록 / 비상정지", 808, 466, 169, 42, 11, C.gray, false, "center");
  arrow(s, 313, 463, 82, 24, C.wine);
  text(s, "좌표 보정값", 318, 440, 70, 18, 9, C.wine, true, "center");
  arrow(s, 678, 463, 82, 24, C.gray);
  text(s, "동작 상태", 683, 440, 70, 18, 9, C.gray, true, "center");

  box(s, 65, 570, 925, 86, C.pale, "none");
  text(s, "핵심 제어 경계", 82, 580, 150, 22, 12, C.wine, true);
  text(s, "AMR의 절대 정차 위치는 이동 제어가 담당하고, 매거진 작업점의 잔여 오차는 QR 좌표를 읽어 협동로봇의 접근 경로에 반영합니다.", 82, 607, 875, 32, 12.5, C.charcoal, false);
  note(s, "Sources: AMR/Service/MoveSequenceRunner.cs, CameraService.cs, CobotService.cs; docs/MainSequence.md; docs/cobot_interface.md.");
}

// 3. Two-stage precision concept
{
  const s = p.slides.add();
  header(s, "±50 mm 정차 오차를 흡수하는 2단계 위치 결정", 3, "AMR은 작업 가능 영역에 진입하고, QR 보정이 실제 PICKUP / PLACE 기준점을 복원");

  sectionLabel(s, 1, "1차 위치 결정: AMR 정차", 65, 105, 350);
  box(s, 75, 155, 395, 330, C.light, "none");
  // coordinate cross and envelope
  line(s, 270, 190, 0, 235, C.mid, 1);
  line(s, 150, 307, 240, 0, C.mid, 1);
  box(s, 195, 232, 150, 150, "none", C.wine, true);
  text(s, "±50 mm\n정차 오차 영역", 205, 276, 130, 62, 18, C.wine, true, "center");
  box(s, 247, 284, 46, 46, C.dark, "none", true);
  text(s, "AMR", 249, 296, 42, 20, 9, C.white, true, "center", LATIN);
  text(s, "목표 노드", 243, 193, 54, 20, 9, C.gray, true, "center");
  text(s, "주행 제어는 QR 카메라가\n대상을 볼 수 있는 범위까지 정차", 105, 420, 335, 46, 12, C.charcoal, true, "center");

  arrow(s, 500, 285, 55, 28, C.wine);
  text(s, "잔여 오차\n측정", 493, 322, 68, 42, 10, C.wine, true, "center");

  sectionLabel(s, 2, "2차 위치 결정: QR 비전 보정", 585, 105, 400);
  box(s, 585, 155, 405, 330, C.white, C.mid);
  // camera frame
  box(s, 620, 188, 220, 180, "none", C.dark);
  line(s, 730, 205, 0, 145, C.blue, 1.5);
  line(s, 650, 278, 160, 0, C.blue, 1.5);
  text(s, "카메라 중심", 663, 207, 135, 20, 9, C.blue, true, "center");
  box(s, 748, 244, 58, 58, "none", C.green);
  box(s, 762, 258, 12, 12, C.green, "none");
  box(s, 780, 276, 12, 12, C.green, "none");
  line(s, 730, 273, 47, 5, C.amber, 2);
  text(s, "QR 중심", 748, 307, 62, 18, 9, C.green, true, "center");
  text(s, "Δx, Δy", 748, 217, 64, 18, 11, C.amber, true, "center", LATIN);
  text(s, "θ", 812, 251, 18, 20, 13, C.wine, true, "center", LATIN);
  box(s, 862, 188, 92, 180, C.pale, "none");
  text(s, "산출값", 874, 202, 68, 20, 11, C.wine, true, "center");
  text(s, "dx [mm]\ndy [mm]\ndθ [deg]", 874, 238, 68, 88, 13, C.charcoal, true, "center", LATIN);
  text(s, "협동로봇\n경로 보정", 874, 324, 68, 34, 10, C.gray, true, "center");
  text(s, "카메라 좌표에서 측정한 잔여 위치·각도 오차를\n협동로봇의 PICKUP / PLACE 기준 위치에 합산", 615, 398, 345, 54, 12, C.charcoal, true, "center");

  box(s, 75, 530, 915, 118, C.pale, "none");
  text(s, "정밀도 확보 조건", 92, 544, 160, 24, 13, C.wine, true);
  text(s, "① QR이 카메라 시야와 유효 Depth 범위 안에 위치   ② QR 좌표계와 로봇 작업 좌표계의 부호·축 방향 일치   ③ 보정 후 접근 경로가 기구 간섭 없이 실행", 92, 577, 870, 28, 11.5, C.charcoal, false);
  text(s, "±50 mm는 AMR 정차 허용 범위이며, 최종 작업 정밀도는 카메라 보정·로봇 반복도·치공구 공차를 포함해 POC에서 확인합니다.", 92, 611, 870, 25, 10.5, C.wine, true);
  note(s, "±50 mm는 사용자가 요청한 정차 오차 범위. 최종 정밀도 수치는 제공되지 않아 POC 검증 항목으로 명시함.");
}

// 4. QR calculation and compensation
{
  const s = p.slides.add();
  header(s, "QR 좌표 산출 및 보정값 생성", 4, "RGB-D 영상의 픽셀 오차를 실제 거리로 변환하고 포트·모델별 편차를 추가 보정");

  const stages = [
    ["01", "QR 검출", "원본·이진화·적응형\n전처리로 인식 안정화"],
    ["02", "중심·각도", "4개 꼭짓점으로\n중심점과 회전각 계산"],
    ["03", "Depth 변환", "중심 주변 5×5\nDepth 중앙값 사용"],
    ["04", "5회 샘플링", "200 ms 안정화 후\n100 ms 간격 중앙값"],
    ["05", "오프셋 합산", "PortOffset + ModelOffset\n포함 후 레지스터 전달"],
  ];
  stages.forEach((it, i) => {
    const x = 55 + i * 200;
    box(s, x, 105, 172, 112, i === 4 ? C.pale : C.light, i === 4 ? C.wine : "none");
    text(s, it[0], x + 12, 115, 40, 20, 10, C.wine, true, "left", LATIN);
    text(s, it[1], x + 12, 140, 148, 24, 14, C.charcoal, true, "left");
    text(s, it[2], x + 12, 170, 148, 35, 9.5, C.gray, false, "left");
    if (i < stages.length - 1) arrow(s, x + 178, 148, 16, 16, C.mid);
  });

  sectionLabel(s, 1, "실거리 변환", 65, 254, 300);
  box(s, 65, 295, 440, 140, C.white, C.mid);
  text(s, "dx = (uQR − uc) × Z / fx", 92, 315, 390, 34, 21, C.charcoal, true, "center", LATIN);
  text(s, "dy = (vQR − vc) × Z / fy", 92, 355, 390, 34, 21, C.charcoal, true, "center", LATIN);
  text(s, "Z: QR 중심 Depth 중앙값    fx, fy: 카메라 초점거리", 92, 397, 390, 22, 10.5, C.gray, false, "center");

  sectionLabel(s, 2, "최종 보정값", 545, 254, 300);
  box(s, 545, 295, 445, 140, C.pale, "none");
  text(s, "보정값 = QR 측정값 + 포트 보정 + 모델 보정", 568, 316, 400, 32, 18, C.wine, true, "center");
  text(s, "PortOffset", 578, 365, 105, 28, 11, C.charcoal, true, "center", LATIN);
  text(s, "노드·LEFT/RIGHT", 578, 393, 105, 18, 9, C.gray, false, "center");
  text(s, "+", 690, 371, 28, 28, 18, C.wine, true, "center", LATIN);
  text(s, "ModelOffset", 724, 365, 105, 28, 11, C.charcoal, true, "center", LATIN);
  text(s, "LOAD / UNLOAD", 724, 393, 105, 18, 9, C.gray, false, "center", LATIN);
  text(s, "=", 838, 371, 28, 28, 18, C.wine, true, "center", LATIN);
  text(s, "AI0 / AI1 / AI2", 870, 365, 105, 28, 11, C.charcoal, true, "center", LATIN);
  text(s, "mm / mm / 0.01°", 870, 393, 105, 18, 9, C.gray, false, "center", LATIN);

  sectionLabel(s, 3, "신호 인터페이스", 65, 474, 300);
  const rows = [
    ["AI0", "dx", "mm", "카메라 X 방향 + 포트/모델 보정"],
    ["AI1", "dy", "mm", "카메라 Y 방향 + 포트/모델 보정"],
    ["AI2", "dθ", "0.01°", "QR 상단 변 회전각 + 포트/모델 보정"],
  ];
  box(s, 65, 515, 925, 122, C.white, C.mid);
  box(s, 65, 515, 925, 30, C.dark, "none");
  ["레지스터", "값", "단위", "적용 내용"].forEach((v, i) => text(s, v, [80, 235, 355, 485][i], 519, [135, 100, 110, 475][i], 22, 10.5, C.white, true, "center"));
  rows.forEach((r, idx) => {
    const y = 548 + idx * 28;
    if (idx % 2 === 1) box(s, 66, y, 923, 28, C.light, "none");
    text(s, r[0], 80, y, 135, 28, 10.5, C.wine, true, "center", LATIN);
    text(s, r[1], 235, y, 100, 28, 10.5, C.charcoal, true, "center", LATIN);
    text(s, r[2], 355, y, 110, 28, 10.5, C.charcoal, false, "center", LATIN);
    text(s, r[3], 485, y, 475, 28, 10.5, C.charcoal, false, "left");
  });
  note(s, "Sources: CameraService.cs lines implementing pinhole conversion, 5×5 depth median and preprocessing; MoveSequenceRunner.cs implementing 5 samples, medians, PortOffset/ModelOffset and AI0–AI2 writes.");
}

// 5. Pickup/place sequence
{
  const s = p.slides.add();
  header(s, "QR 기반 매거진 PICKUP / PLACE 시퀀스", 5, "작업 직전에 QR을 다시 읽고 보정값을 적용해 정차 편차와 작업별 편차를 분리");

  const items = [
    ["01", "이동 명령", "ACS moveCmd\nNode · Port · Job"],
    ["02", "AMR 이동", "목표 노드 도착\n±50 mm 범위"],
    ["03", "QR 스캔 위치", "협동로봇 DI16/17\n카메라 시야 정렬"],
    ["04", "QR 보정 계산", "dx · dy · dθ\n포트/모델 보정"],
    ["05", "PICKUP", "보정 좌표로 접근\n그립 후 완료 확인"],
    ["06", "PLACE", "대상 슬롯 이동\n놓기 후 완료 확인"],
    ["07", "작업 완료", "상태 보고\n다음 명령 대기"],
  ];
  items.forEach((it, i) => {
    const x = 43 + i * 145;
    const active = i === 3;
    box(s, x, 128, 122, 128, active ? C.pale : C.light, active ? C.wine : "none");
    text(s, it[0], x + 10, 137, 34, 20, 9.5, C.wine, true, "left", LATIN);
    text(s, it[1], x + 10, 163, 102, 28, 13, C.charcoal, true, "center");
    text(s, it[2], x + 10, 198, 102, 44, 9.5, C.gray, false, "center");
    if (i < items.length - 1) arrow(s, x + 126, 183, 14, 18, active ? C.wine : C.mid);
  });

  sectionLabel(s, 1, "작업별 QR 재판독 원칙", 65, 302, 350);
  box(s, 65, 344, 925, 114, C.white, C.mid);
  text(s, "PICKUP 전", 84, 359, 126, 24, 13, C.wine, true, "center");
  text(s, "현재 정차 자세를 기준으로 포트의 QR을 읽어 PICK 위치를 보정", 225, 354, 720, 34, 12.5, C.charcoal, true);
  line(s, 85, 398, 860, 0, C.mid, 1);
  text(s, "PLACE 전", 84, 410, 126, 24, 13, C.wine, true, "center");
  text(s, "작업 방향과 모델에 맞는 오프셋을 적용해 PLACE 목표 위치를 보정", 225, 405, 720, 34, 12.5, C.charcoal, true);

  sectionLabel(s, 2, "실행 조건과 인터록", 65, 495, 350);
  const checks = [
    ["QR 인식", "유효 샘플 확보 및 좌표 산출"],
    ["로봇 상태", "Busy / Complete / Error 확인"],
    ["매거진 확인", "Depth ROI로 존재 여부 판정"],
    ["안전 조건", "I/O 인터록과 비상정지 상태 확인"],
  ];
  checks.forEach((it, i) => {
    const x = 65 + i * 232;
    box(s, x, 538, 210, 92, i === 0 ? C.pale : C.light, "none");
    box(s, x + 14, 552, 22, 22, i === 0 ? C.wine : C.dark, "none", true);
    text(s, "✓", x + 14, 551, 22, 22, 11, C.white, true, "center", LATIN);
    text(s, it[0], x + 46, 548, 148, 28, 12.5, C.charcoal, true);
    text(s, it[1], x + 14, 582, 182, 35, 9.8, C.gray, false, "center");
  });
  note(s, "Sources: docs/MainSequence.md; MoveSequenceRunner.cs (Steps 1–10, QR read, pickup/place selection, magazine detection and I/O checks). The current code includes last-value fallback when all five QR samples fail; production acceptance should define whether fallback is allowed.");
}

// 6. Verification and accuracy budget
{
  const s = p.slides.add();
  header(s, "위치 정밀도 검증 기준 및 POC 계획", 6, "±50 mm 전 구간에서 QR 검출, 보정 적용, 매거진 작업 성공을 반복 검증");

  sectionLabel(s, 1, "시험 매트릭스", 65, 105, 300);
  box(s, 65, 148, 580, 220, C.white, C.mid);
  box(s, 65, 148, 580, 34, C.dark, "none");
  const hx = [75, 220, 340, 455];
  const hw = [130, 105, 100, 175];
  ["시험 항목", "조건", "반복", "판정 지표"].forEach((v, i) => text(s, v, hx[i], 153, hw[i], 24, 10.5, C.white, true, "center"));
  const testRows = [
    ["정차 오프셋", "X/Y ±50 mm", "각 조건 10회", "QR 검출률 · 보정값"],
    ["회전 편차", "±θ 조건", "각 조건 10회", "각도 오차 · 접근 안정성"],
    ["PICKUP", "좌/우 슬롯", "LOAD/UNLOAD", "그립 성공 · 간섭 없음"],
    ["PLACE", "좌/우 슬롯", "모델별", "안착 편차 · 완료 신호"],
    ["환경 변화", "조도·반사", "대표 조건", "검출률 · 재시도율"],
  ];
  testRows.forEach((r, ri) => {
    const y = 184 + ri * 36;
    if (ri % 2) box(s, 66, y, 578, 36, C.light, "none");
    r.forEach((v, i) => text(s, v, hx[i], y, hw[i], 36, 10, i === 0 ? C.wine : C.charcoal, i === 0, i === 3 ? "left" : "center"));
  });

  sectionLabel(s, 2, "정밀도 오차 예산", 690, 105, 300);
  box(s, 690, 148, 300, 220, C.pale, "none");
  const budget = [
    ["카메라·Depth", "픽셀/거리 노이즈"],
    ["좌표 변환", "축 방향·스케일"],
    ["협동로봇", "반복도·TCP"],
    ["그리퍼/치공구", "기구 공차"],
  ];
  budget.forEach((r, i) => {
    const y = 166 + i * 42;
    text(s, r[0], 708, y, 112, 28, 11.5, C.wine, true);
    text(s, r[1], 825, y, 145, 28, 11, C.charcoal, false);
    if (i < budget.length - 1) line(s, 708, y + 34, 262, 0, "#E1C6CC", 1);
  });
  text(s, "최종 작업 정밀도 = 위 항목의 결합 결과", 708, 334, 262, 24, 11, C.wine, true, "center");

  sectionLabel(s, 3, "권장 합격 기준", 65, 408, 300);
  const criteria = [
    ["검출", "시험 위치 전 구간에서 QR 식별 및 유효 Depth 확보"],
    ["좌표", "보정값의 부호·크기가 인위 오프셋 방향과 일치"],
    ["작업", "PICKUP / PLACE 성공 및 충돌·과도한 재접근 없음"],
    ["반복", "동일 조건의 최종 안착 편차가 고객 요구 공차 이내"],
  ];
  criteria.forEach((r, i) => {
    const y = 450 + i * 45;
    box(s, 65, y, 98, 32, i === 3 ? C.wine : C.dark, "none");
    text(s, r[0], 65, y, 98, 32, 11, C.white, true, "center");
    text(s, r[1], 180, y - 1, 810, 34, 11.5, C.charcoal, i === 3);
  });

  box(s, 65, 645, 925, 42, C.wine, "none");
  text(s, "POC 결과로 카메라 시야, 허용 오프셋, 재시도 기준, 포트·모델 보정값을 확정한 뒤 양산 기준서에 반영", 85, 651, 885, 30, 12, C.white, true, "center");
  note(s, "정량 합격 공차와 회전 허용 범위는 사용자 입력이 없어 고객 요구사항으로 남김. 반복 횟수는 검증 계획 제안이며 실제 POC 협의 시 조정 가능.");
}

const stagingDir = path.join(buildDir, "finalizer");
await fs.mkdir(stagingDir, { recursive: true });
const candidatePath = path.join(stagingDir, "candidate.pptx");
await (await PresentationFile.exportPptx(p)).save(candidatePath);

const result = await finalizePresentation({
  workspaceDir,
  candidatePath,
  finalPath: FINAL_PPTX,
  pythonExecutable: RUNTIME_PYTHON,
  integrityValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_layout_geometry.py"),
  layoutArgs: [
    "--expected-slide-size-emu", "9906000,6858000",
    "--validate-heading-fit",
  ],
  explicitTotalSlideCount: 6,
  requiredNativeTableOwnerSlides: [],
  requiredNativeChartOwnerSlides: [],
  fontPolicy: {
    basis: "design",
    families: [LATIN, FONT],
    scriptFonts: { ea: FONT },
  },
  verifyArtifactToolImport: true,
  receiptPath: path.join(stagingDir, "validation_v2.json"),
});

console.log(JSON.stringify({ final: FINAL_PPTX, warnings: result.warnings ?? [] }, null, 2));
