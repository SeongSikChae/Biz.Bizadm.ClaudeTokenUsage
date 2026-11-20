import { render, screen, within } from "@testing-library/react";
import { sampleSeries } from "../testData";
import { formatBucketLabel } from "../format";
import { UsageChart } from "./UsageChart";

describe("UsageChart", () => {
  it("lists every series and the cost line", () => {
    render(<UsageChart data={sampleSeries} />);
    const legend = within(screen.getByRole("list", { name: "계열" }));
    expect(legend.getByText("input")).toBeInTheDocument();
    expect(legend.getByText("output")).toBeInTheDocument();
    expect(legend.getByText("비용 (USD)")).toBeInTheDocument();
  });

  it("omits the cost line when cost is null", () => {
    render(<UsageChart data={{ ...sampleSeries, buckets: sampleSeries.buckets.map((b) => ({ ...b, costUsd: null })) }} />);
    expect(within(screen.getByRole("list", { name: "계열" })).queryByText("비용 (USD)")).not.toBeInTheDocument();
  });

  it("formats bucket labels in KST per unit", () => {
    expect(formatBucketLabel("hour", "2026-11-20T01:00:00Z")).toBe("11-20 10:00");
    expect(formatBucketLabel("week", "2026-11-16T00:00:00+09:00")).toBe("2026-11-16");
    expect(formatBucketLabel("month", "2026-11-01T00:00:00+09:00")).toBe("2026-11");
    expect(formatBucketLabel("year", "2026-01-01T00:00:00+09:00")).toBe("2026");
  });
});
