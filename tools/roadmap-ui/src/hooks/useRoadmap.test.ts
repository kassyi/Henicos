import { renderHook, act } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { useRoadmap } from "./useRoadmap";

describe("useRoadmap hook", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("normalizes legacy tasks with completed: true to incomplete if no valid status", async () => {
    const mockData = [
      {
        id: "T1",
        title: "Legacy Task",
        completed: true,
        metrics: { blocker: 1, difficulty: 1, generality: 1, score: 10 },
      },
    ];

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockData,
    });

    const { result } = renderHook(() => useRoadmap());

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(result.current.loading).toBe(false);
    expect(result.current.tasks.length).toBeGreaterThan(0);

    const task = result.current.tasks[0];
    expect(task.status).toBe("incomplete");
  });

  it("filters tasks by progress status correctly", async () => {
    const mockData = [
      {
        id: "T1",
        title: "Task 1",
        status: "implemented",
        section: "Group 1",
        metrics: { blocker: 1, difficulty: 1, generality: 1, score: 10 },
      },
      {
        id: "T2",
        title: "Task 2",
        status: "tuned",
        section: "Group 1",
        metrics: { blocker: 1, difficulty: 1, generality: 1, score: 10 },
      },
      {
        id: "T3",
        title: "Task 3",
        status: "incomplete",
        section: "Group 1",
        metrics: { blocker: 1, difficulty: 1, generality: 1, score: 10 },
      },
      {
        id: "T4",
        title: "Task 4",
        status: "rejected",
        section: "Group 1",
        metrics: { blocker: 1, difficulty: 1, generality: 1, score: 10 },
      },
    ];

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockData,
    });

    const { result } = renderHook(() => useRoadmap());

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    act(() => {
      result.current.setProgressFilter("incomplete");
    });

    expect(result.current.filteredTasks.length).toBe(1);
    expect(result.current.filteredTasks[0].id).toBe("T3");

    act(() => {
      result.current.setProgressFilter("implemented_plus");
    });

    expect(result.current.filteredTasks.length).toBe(2);

    act(() => {
      result.current.setProgressFilter("tuned");
    });

    expect(result.current.filteredTasks.length).toBe(1);
    expect(result.current.filteredTasks[0].id).toBe("T2");

    act(() => {
      result.current.setProgressFilter("rejected");
    });

    expect(result.current.filteredTasks.length).toBe(1);
    expect(result.current.filteredTasks[0].id).toBe("T4");

    act(() => {
      result.current.setProgressFilter("all");
    });

    expect(result.current.filteredTasks.length).toBe(4);
  });

  it("handles fetch errors gracefully", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    globalThis.fetch = vi.fn().mockRejectedValue(new Error("Network error"));

    const { result } = renderHook(() => useRoadmap());

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(result.current.loading).toBe(false);
    expect(result.current.tasks.length).toBe(0);
    expect(consoleSpy).toHaveBeenCalled();
    consoleSpy.mockRestore();
  });
});
