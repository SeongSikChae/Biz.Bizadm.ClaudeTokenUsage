import { BarChart, type BarSeriesOption } from "echarts/charts";
import {
  AxisPointerComponent,
  DataZoomComponent,
  GridComponent,
  LegendComponent,
  TooltipComponent,
  type DataZoomComponentOption,
  type GridComponentOption,
  type LegendComponentOption,
  type TooltipComponentOption,
} from "echarts/components";
import * as echarts from "echarts/core";
import { CanvasRenderer } from "echarts/renderers";

// 쓰는 모듈만 등록해 번들을 줄인다. 테스트에서는 이 모듈 전체를 가짜로 바꾼다(src/test/setup.ts).
echarts.use([BarChart, GridComponent, TooltipComponent, AxisPointerComponent, DataZoomComponent, LegendComponent, CanvasRenderer]);

export type ChartOption = echarts.ComposeOption<
  BarSeriesOption | GridComponentOption | TooltipComponentOption | DataZoomComponentOption | LegendComponentOption
>;

export type ChartInstance = Pick<echarts.ECharts, "setOption" | "resize" | "dispose" | "dispatchAction">;

export function init(element: HTMLElement): ChartInstance {
  return echarts.init(element, undefined, { renderer: "canvas" });
}
