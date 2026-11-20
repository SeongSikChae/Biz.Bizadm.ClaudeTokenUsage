import { render, screen } from "@testing-library/react";
import { sampleSeries } from "../testData";
import { UsageSummary } from "./UsageSummary";

describe("UsageSummary", () => {
  it("shows token and cost totals", () => {
    render(<UsageSummary data={sampleSeries} tokenTypeFiltered={false} />);
    expect(screen.getByText("130")).toBeInTheDocument();
    expect(screen.getByText("$0.2500")).toBeInTheDocument();
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("shows the notice and no cost when filtered by token type", () => {
    render(
      <UsageSummary
        data={{ ...sampleSeries, totals: { tokens: 100, costUsd: null } }}
        tokenTypeFiltered
      />,
    );
    expect(screen.getByText("—")).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("토큰 종류로 거르면 비용을 볼 수 없습니다");
  });
});
