"use client";
import { createContext, useContext, useEffect, type ReactNode } from "react";
import { init, rahgir, type RahgirConfig } from "./core.js";

const Ctx = createContext(rahgir);

export interface RahgirProviderProps extends RahgirConfig {
  children?: ReactNode;
  /** Set to false to disable (e.g. in development). Default true. */
  enabled?: boolean;
}

/**
 * Put once in app/layout.tsx (App Router) or pages/_app.tsx (Pages Router).
 * Page views on client-side navigation are captured automatically.
 */
export function RahgirProvider({ children, enabled = true, ...config }: RahgirProviderProps) {
  useEffect(() => {
    if (enabled) init(config);
    // config is read once on mount on purpose
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled]);
  return <Ctx.Provider value={rahgir}>{children}</Ctx.Provider>;
}

/** const { track, identify, reset } = useRahgir(); */
export function useRahgir() {
  return useContext(Ctx);
}
