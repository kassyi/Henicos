import { describe, it, expect } from "vitest";
import { DOMAIN_LABELS } from "../../src/types";

describe("DOMAIN_LABELS", () => {
  it("maps registered domain codes to localized label strings", () => {
    expect(DOMAIN_LABELS["SYN"]).toBe("SYN (構文)");
    expect(DOMAIN_LABELS["GSH"]).toBe("GSH (トランスパイラ)");
    expect(DOMAIN_LABELS["GEN"]).toBe("GEN (ジェネレータ基盤)");
    expect(DOMAIN_LABELS["GBI"]).toBe("GBI (GpuBigInteger)");
    expect(DOMAIN_LABELS["GBF"]).toBe("GBF (GpuBigFloat)");
    expect(DOMAIN_LABELS["BCL"]).toBe("BCL (基本クラスライブラリ)");
    expect(DOMAIN_LABELS["MEM"]).toBe("MEM (メモリ管理)");
  });

  it("handles unknown or empty domain keys with fallback value", () => {
    const getDomainLabel = (domain?: string) => {
      if (!domain) return "Unknown";
      return DOMAIN_LABELS[domain] || domain;
    };

    expect(getDomainLabel("CUSTOM")).toBe("CUSTOM");
    expect(getDomainLabel("")).toBe("Unknown");
    expect(getDomainLabel(undefined)).toBe("Unknown");
    expect(DOMAIN_LABELS["NON_EXISTENT"]).toBeUndefined();
  });
});
