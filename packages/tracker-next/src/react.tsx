"use client";
import { createContext, useContext, useEffect, type ReactNode } from "react";
import { init, tracker, type TrackerConfig } from "./core.js";

const Ctx = createContext(tracker);

export interface TrackerProviderProps extends TrackerConfig {
  children?: ReactNode;
  /** Set to false to disable (e.g. in development). Default true. */
  enabled?: boolean;
}

/**
 * Put once in app/layout.tsx (App Router) or pages/_app.tsx (Pages Router).
 * Page views on client-side navigation are captured automatically.
 */
export function TrackerProvider({ children, enabled = true, ...config }: TrackerProviderProps) {
  useEffect(() => {
    if (enabled) init(config);
    // config is read once on mount on purpose
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled]);
  return <Ctx.Provider value={tracker}>{children}</Ctx.Provider>;
}

/** const { track, identify, reset } = useTracker(); */
export function useTracker() {
  return useContext(Ctx);
}
