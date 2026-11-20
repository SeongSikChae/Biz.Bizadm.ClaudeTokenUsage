import { assignColors } from "./seriesColors";

describe("assignColors", () => {
  it("assigns colors in response order on reset and wraps after eight", () => {
    const names = ["a", "b", "c", "d", "e", "f", "g", "h", "i"];
    const result = assignColors({ x: 3 }, names, true);
    expect(names.map((name) => result[name])).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 0]);
    expect(result).not.toHaveProperty("x");
  });

  it("keeps existing colors and gives a new series the smallest unused color", () => {
    expect(assignColors({ input: 0, output: 2 }, ["input", "output", "cacheRead"], false)).toEqual({
      input: 0,
      output: 2,
      cacheRead: 1,
    });
  });

  it("does not let a new name sorted before existing names take their colors", () => {
    expect(assignColors({ input: 0, output: 1 }, ["cacheCreation", "input", "output"], false)).toEqual({
      cacheCreation: 2,
      input: 0,
      output: 1,
    });
  });

  it("drops series missing from the response so their colors can be reused", () => {
    const afterDrop = assignColors({ a: 0, b: 1 }, ["a", "c"], false);
    expect(afterDrop).toEqual({ a: 0, c: 1 });
    // 빠졌던 b가 다시 나타나면 새 계열처럼 빈 색을 받는다.
    expect(assignColors(afterDrop, ["a", "b", "c"], false)).toEqual({ a: 0, b: 2, c: 1 });
  });

  it("wraps when all eight colors are in use", () => {
    const prev = Object.fromEntries(["a", "b", "c", "d", "e", "f", "g", "h"].map((name, index) => [name, index]));
    const result = assignColors(prev, [...Object.keys(prev), "i"], false);
    expect(result.i).toBe(0);
    expect(result.h).toBe(7);
  });
});
