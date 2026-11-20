import { useSyncExternalStore } from "react";

export type Theme = "light" | "dark";

const STORAGE_KEY = "theme";
const DARK_QUERY = "(prefers-color-scheme: dark)";

let current: Theme = "light";
let media: MediaQueryList | null = null;
const listeners = new Set<() => void>();

function readStored(): Theme | null {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    // 사생활 보호 모드 등에서 저장소를 쓸 수 없으면 고른 값이 없는 것으로 본다.
    return null;
  }
}

/**
 * `<html>` 클래스를 먼저 바꾼 뒤 구독자에게 알린다. React가 다시 그릴 때 이미 새 테마의 CSS 변수가 적용돼 있어야
 * 차트가 getComputedStyle로 새 색을 읽는다(자식 effect가 부모 effect보다 먼저 실행되므로 effect에서 바꾸면 늦다).
 */
function apply(theme: Theme): void {
  current = theme;
  document.documentElement.classList.toggle("dark", theme === "dark");
  listeners.forEach((listener) => listener());
}

function onSystemChange(event: MediaQueryListEvent): void {
  // 사용자가 고른 적이 없을 때만 운영체제 설정을 따른다. 운영체제 값은 저장하지 않는다.
  if (readStored() === null) apply(event.matches ? "dark" : "light");
}

/** 저장된 값(없으면 운영체제 설정)으로 테마를 정하고 운영체제 변경을 듣는다. 테스트에서 다시 부를 수 있다. */
export function initTheme(): void {
  media?.removeEventListener("change", onSystemChange);
  media = typeof window.matchMedia === "function" ? window.matchMedia(DARK_QUERY) : null;
  media?.addEventListener("change", onSystemChange);
  apply(readStored() ?? (media?.matches ? "dark" : "light"));
}

/** 사용자가 고른 테마. 브라우저에 저장해 다시 열어도 유지한다. */
export function setTheme(theme: Theme): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // 저장할 수 없어도 이번 화면에는 적용한다.
  }
  apply(theme);
}

export function getTheme(): Theme {
  return current;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, getTheme, getTheme);
}

initTheme();
