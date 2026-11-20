import { combineReducers, configureStore } from "@reduxjs/toolkit";
import usageReducer from "@/features/usage/usageSlice";

const rootReducer = combineReducers({
  usage: usageReducer,
});

export function createStore(preloadedState?: Partial<RootState>) {
  return configureStore({ reducer: rootReducer, preloadedState });
}

export const store = createStore();

export type RootState = ReturnType<typeof rootReducer>;
export type AppStore = ReturnType<typeof createStore>;
export type AppDispatch = AppStore["dispatch"];
