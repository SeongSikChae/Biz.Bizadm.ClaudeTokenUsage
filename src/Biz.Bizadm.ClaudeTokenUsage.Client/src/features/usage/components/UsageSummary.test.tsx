import { render, screen, within } from "@testing-library/react";
import { sampleSeries } from "../testData";
import { UsageSummary } from "./UsageSummary";

const colorIndex = { input: 0, output: 1 };

describe("UsageSummary", () => {
  it("shows token and cost totals in cards", () => {
    const { container } = render(<UsageSummary data={sampleSeries} tokenTypeFiltered={false} colorIndex={colorIndex} />);
    expect(screen.getByText("130")).toBeInTheDocument();
    expect(screen.getByText("$0.2500")).toBeInTheDocument();
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
    // 요약 칸도 다른 영역과 같은 Card 컴포넌트다.
    expect(screen.getByText("토큰 총합").closest('[data-slot="card"]')).not.toBeNull();
    expect(screen.getByText("비용 총합 (USD)").closest('[data-slot="card"]')).not.toBeNull();
    expect(container.querySelectorAll('[data-slot="card"]')).toHaveLength(3);
  });

  it("shows per-series totals and ratios", () => {
    render(<UsageSummary data={sampleSeries} tokenTypeFiltered={false} colorIndex={colorIndex} />);
    const list = within(screen.getByRole("list", { name: "계열별 합계" }));
    expect(list.getByText("input").closest("li")).toHaveTextContent("100 76.9%");
    expect(list.getByText("output").closest("li")).toHaveTextContent("30 23.1%");
  });

  it("shows a dash instead of a ratio when the total is zero", () => {
    const empty = { ...sampleSeries, buckets: sampleSeries.buckets.map((bucket) => ({ ...bucket, tokens: {}, totalTokens: 0 })), totals: { tokens: 0, costUsd: 0 } };
    render(<UsageSummary data={empty} tokenTypeFiltered={false} colorIndex={colorIndex} />);
    const list = within(screen.getByRole("list", { name: "계열별 합계" }));
    expect(list.getByText("input").closest("li")).toHaveTextContent("0 —");
  });

  it("shows the notice and no cost when filtered by token type", () => {
    render(<UsageSummary data={{ ...sampleSeries, totals: { tokens: 100, costUsd: null } }} tokenTypeFiltered colorIndex={colorIndex} />);
    expect(screen.getByText("—")).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("토큰 종류로 거르면 비용을 볼 수 없습니다");
  });
});
