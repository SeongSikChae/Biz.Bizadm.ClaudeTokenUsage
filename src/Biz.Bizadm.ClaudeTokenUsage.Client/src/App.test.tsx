import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import App from "./App";
import { createStore } from "./app/store";
import { sampleMeta, sampleSeries } from "./features/usage/testData";

describe("App", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn((url: string) =>
        Promise.resolve(new Response(JSON.stringify(url.startsWith("/api/usage/meta") ? sampleMeta : sampleSeries), { status: 200 })),
      ),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the usage screen with filters, totals and chart", async () => {
    render(
      <Provider store={createStore()}>
        <App />
      </Provider>,
    );

    expect(screen.getByRole("heading", { name: "Claude Code 토큰 사용량" })).toBeInTheDocument();
    expect(await screen.findByText("130")).toBeInTheDocument();
    expect(screen.getByText("구간별 사용량")).toBeInTheDocument();
    expect(await screen.findByText("a@example.com")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(expect.stringMatching(/^\/api\/usage\/series\?unit=hour/), expect.anything());
  });
});
