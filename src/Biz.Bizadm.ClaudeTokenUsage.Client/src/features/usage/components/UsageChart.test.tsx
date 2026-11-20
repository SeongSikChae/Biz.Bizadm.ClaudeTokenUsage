import { fireEvent, render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import type { Mock } from "vitest";
import { createStore } from "@/app/store";
import { init } from "@/lib/echarts";
import { sampleSeries } from "../testData";
import { formatBucketLabel, formatCompact, formatKstTime, formatPercent, formatTokens } from "../format";
import type { UsageSeries } from "../types";
import { UsageChart } from "./UsageChart";

const noCost: UsageSeries = { ...sampleSeries, buckets: sampleSeries.buckets.map((bucket) => ({ ...bucket, costUsd: null })) };

function lastChart(): { setOption: Mock } {
  const results = vi.mocked(init).mock.results;
  return results[results.length - 1].value as { setOption: Mock };
}

function lastOption(): { legend: { selected: Record<string, boolean> } } {
  const calls = lastChart().setOption.mock.calls;
  return calls[calls.length - 1][0];
}

function renderChart(data: UsageSeries) {
  const store = createStore();
  const view = render(
    <Provider store={store}>
      <UsageChart data={data} />
    </Provider>,
  );
  const rerender = (next: UsageSeries) =>
    view.rerender(
      <Provider store={store}>
        <UsageChart data={next} />
      </Provider>,
    );
  return { store, rerender };
}

describe("UsageChart", () => {
  it("lists every series and the cost bar in the legend", () => {
    renderChart(sampleSeries);
    const legend = within(screen.getByRole("list", { name: "계열" }));
    expect(legend.getByRole("button", { name: "input" })).toBeInTheDocument();
    expect(legend.getByRole("button", { name: "output" })).toBeInTheDocument();
    expect(legend.getByText("비용 (USD)")).toBeInTheDocument();
  });

  it("omits the cost legend when cost is null", () => {
    renderChart(noCost);
    expect(within(screen.getByRole("list", { name: "계열" })).queryByText("비용 (USD)")).not.toBeInTheDocument();
  });

  it("hides and shows a series from the legend", () => {
    const { store } = renderChart(sampleSeries);
    const button = screen.getByRole("button", { name: "output" });
    expect(button).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(button);
    expect(button).toHaveAttribute("aria-pressed", "false");
    expect(store.getState().usage.hidden).toEqual(["output"]);
    expect(lastOption().legend.selected).toEqual({ input: true, output: false });

    fireEvent.click(button);
    expect(button).toHaveAttribute("aria-pressed", "true");
    expect(lastOption().legend.selected).toEqual({ input: true, output: true });
  });

  it("merges series only while the grid layout stays and redraws when cost appears or disappears", () => {
    const { rerender } = renderChart(sampleSeries);
    const chart = lastChart();
    expect(chart.setOption.mock.calls[0][1]).toEqual({ notMerge: true }); // 처음 그릴 때

    rerender({ ...sampleSeries, totals: { tokens: 131, costUsd: 0.3 } });
    expect(chart.setOption.mock.calls.at(-1)?.[1]).toEqual({ replaceMerge: ["series"] });

    rerender(noCost); // 비용 있음 → 없음: 비용 그리드·축이 남지 않게 새로 그린다
    expect(chart.setOption.mock.calls.at(-1)?.[1]).toEqual({ notMerge: true });

    rerender({ ...noCost });
    expect(chart.setOption.mock.calls.at(-1)?.[1]).toEqual({ replaceMerge: ["series"] });
  });

  it("formats numbers for axes, tooltips, ratios and refresh time", () => {
    expect(formatCompact(160000)).toBe("160K");
    expect(formatCompact(1_500_000)).toBe("1.5M");
    expect(formatCompact(2_000_000_000)).toBe("2B");
    expect(formatCompact(950)).toBe("950");
    expect(formatTokens(152152)).toBe("152,152");
    expect(formatPercent(1, 4)).toBe("25.0%");
    expect(formatPercent(0, 0)).toBe("—");
    expect(formatKstTime("2026-11-20T13:22:05Z")).toBe("11-20 22:22:05");
  });

  it("formats bucket labels in KST per unit", () => {
    expect(formatBucketLabel("hour", "2026-11-20T01:00:00Z")).toBe("11-20 10:00");
    expect(formatBucketLabel("week", "2026-11-16T00:00:00+09:00")).toBe("2026-11-16");
    expect(formatBucketLabel("month", "2026-11-01T00:00:00+09:00")).toBe("2026-11");
    expect(formatBucketLabel("year", "2026-01-01T00:00:00+09:00")).toBe("2026");
  });
});
