import { useState, useEffect, useRef, useMemo, useDeferredValue } from "react";
import type { RoadmapTask, TaskStatus, ToastState } from "../types";
import julesPromptTemplate from "../templates/jules-prompt-template.md?raw";

export function useRoadmap() {
  const [tasks, setTasks] = useState<RoadmapTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [saveStatus, setSaveStatus] = useState<"saved" | "saving" | "error">("saved");
  const isDirty = useRef(false);

  // Undo / Redo History
  const [history, setHistory] = useState<{ past: RoadmapTask[][]; future: RoadmapTask[][] }>({
    past: [],
    future: [],
  });

  // Google Keep-like Slide Out Animation & Grace Period
  const [gracePeriodIds, setGracePeriodIds] = useState<Set<string>>(new Set());
  const [exitingIds, setExitingIds] = useState<Set<string>>(new Set());
  const exitTimers = useRef<Record<string, number>>({});

  const [searchQuery, setSearchQuery] = useState("");
  const [minBlocker, setMinBlocker] = useState<number>(1);
  const [minDifficulty, setMinDifficulty] = useState<number>(1);
  const [minGenerality, setMinGenerality] = useState<number>(1);
  const [showAdvancedFilters, setShowAdvancedFilters] = useState(true);
  const [statusFilters, setStatusFilters] = useState<TaskStatus[]>(["incomplete"]);

  const [toast, setToast] = useState<ToastState>({
    open: false,
    message: "",
  });

  // Retained IDs when toggling between implemented and tuned in filtered views
  const [retainedIds, setRetainedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    setRetainedIds(new Set());
  }, [statusFilters]);

  // AI Cart Feature
  const [cartIds, setCartIds] = useState<string[]>([]);
  const [lastCartClickedId, setLastCartClickedId] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/load")
      .then((res) => {
        if (!res.ok) throw new Error("Data not found");
        return res.json();
      })
      .then((data: any[]) => {
        const normalized: RoadmapTask[] = data.map((t) => {
          let status: TaskStatus = "incomplete";
          if (t.status === "tuned" || t.status === "implemented" || t.status === "incomplete") {
            status = t.status;
          } else if (t.completed) {
            status = "implemented";
          }
          return {
            ...t,
            status,
            completed: status !== "incomplete",
          };
        });
        setTasks(normalized);
        setLoading(false);
      })
      .catch((err) => {
        console.error(err);
        setLoading(false);
      });
  }, []);

  // Auto-Save Effect (Debounced)
  useEffect(() => {
    if (loading || !isDirty.current) return;

    setSaveStatus("saving");
    const timer = setTimeout(async () => {
      try {
        isDirty.current = false;
        const res = await fetch("/api/save", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(tasks),
        });
        if (res.ok) {
          setSaveStatus("saved");
        } else {
          setSaveStatus("error");
        }
      } catch (err) {
        console.error(err);
        setSaveStatus("error");
      }
    }, 500);

    return () => clearTimeout(timer);
  }, [tasks, loading]);

  // Undo / Redo logic
  const pushHistory = (currentTasks: RoadmapTask[]) => {
    setHistory((prev) => ({
      past: [...prev.past.slice(-30), currentTasks],
      future: [],
    }));
  };

  const handleUndo = () => {
    setHistory((prev) => {
      if (prev.past.length === 0) return prev;
      const previous = prev.past[prev.past.length - 1];
      const newPast = prev.past.slice(0, prev.past.length - 1);
      const newFuture = [tasks, ...prev.future.slice(0, 30)];
      isDirty.current = true;
      setTasks(previous);
      setToast({
        open: true,
        message: "直前の操作を元に戻しました (Undo)",
      });
      return { past: newPast, future: newFuture };
    });
  };

  const handleRedo = () => {
    setHistory((prev) => {
      if (prev.future.length === 0) return prev;
      const next = prev.future[0];
      const newFuture = prev.future.slice(1);
      const newPast = [...prev.past.slice(-30), tasks];
      isDirty.current = true;
      setTasks(next);
      setToast({
        open: true,
        message: "操作をやり直しました (Redo)",
      });
      return { past: newPast, future: newFuture };
    });
  };

  // Keyboard Shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target.tagName === "INPUT" || target.tagName === "TEXTAREA") {
        return;
      }

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "z") {
        if (e.shiftKey) {
          e.preventDefault();
          handleRedo();
        } else {
          e.preventDefault();
          handleUndo();
        }
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "y") {
        e.preventDefault();
        handleRedo();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [tasks, history]);

  const updateTaskStatus = (id: string, newStatus: TaskStatus, recordHistory = true) => {
    const targetTask = tasks.find((t) => t.id === id);
    if (!targetTask) return;
    const oldStatus = targetTask.status || (targetTask.completed ? "implemented" : "incomplete");
    if (oldStatus === newStatus) return;

    if (recordHistory) {
      pushHistory(tasks);
    }

    const willBeFilteredOut =
      (statusFilters.includes("incomplete") && newStatus !== "incomplete") ||
      (!statusFilters.includes("incomplete") && newStatus === "incomplete");

    if (willBeFilteredOut) {
      setRetainedIds((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
      setGracePeriodIds((prev) => new Set(prev).add(id));

      if (exitTimers.current[id]) clearTimeout(exitTimers.current[id]);

      exitTimers.current[id] = setTimeout(() => {
        setGracePeriodIds((prev) => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
        setExitingIds((prev) => new Set(prev).add(id));
        setTimeout(() => {
          setExitingIds((prev) => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
        }, 350);
      }, 4000); // 4 seconds grace period
    } else {
      if (!statusFilters.includes("incomplete")) {
        setRetainedIds((prev) => new Set(prev).add(id));
      }
      if (exitTimers.current[id]) {
        clearTimeout(exitTimers.current[id]);
        delete exitTimers.current[id];
      }
      setGracePeriodIds((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
      setExitingIds((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
    }

    isDirty.current = true;
    setTasks((prev) =>
      prev.map((t) =>
        t.id === id
          ? {
              ...t,
              status: newStatus,
              completed: newStatus !== "incomplete",
            }
          : t,
      ),
    );

    const statusLabel =
      newStatus === "implemented" ? "🛠️ 実装完了" : newStatus === "tuned" ? "⚡ パフォ完了" : "未完了";

    setToast({
      open: true,
      message: `「${targetTask.id}」を ${statusLabel} にしました`,
      actionText: "元に戻す",
      onAction: () => {
        updateTaskStatus(id, oldStatus, true);
      },
    });
  };

  const toggleCart = (id: string) => {
    setCartIds((prev) => (prev.includes(id) ? prev.filter((i) => i !== id) : [...prev, id]));
  };

  const calculateTier = (score: number) => {
    if (score >= 36) return 1;
    if (score >= 28) return 2;
    if (score >= 20) return 3;
    return 4;
  };

  const deferredSearchQuery = useDeferredValue(searchQuery);
  const deferredStatusFilters = useDeferredValue(statusFilters);
  const deferredMinBlocker = useDeferredValue(minBlocker);
  const deferredMinDifficulty = useDeferredValue(minDifficulty);
  const deferredMinGenerality = useDeferredValue(minGenerality);

  const filteredTasks = useMemo(() => {
    return tasks
      .filter((t) => {
        const matchesSearch =
          t.title.toLowerCase().includes(deferredSearchQuery.toLowerCase()) ||
          t.id.toLowerCase().includes(deferredSearchQuery.toLowerCase()) ||
          t.notes.toLowerCase().includes(deferredSearchQuery.toLowerCase());

        const taskStatus = t.status || (t.completed ? "implemented" : "incomplete");
        const isExiting = exitingIds.has(t.id);

        const matchesCompletion =
          isExiting ||
          gracePeriodIds.has(t.id) ||
          (deferredStatusFilters.includes("incomplete") && taskStatus === "incomplete") ||
          (deferredStatusFilters.includes("implemented") && (taskStatus === "implemented" || retainedIds.has(t.id))) ||
          (deferredStatusFilters.includes("tuned") && (taskStatus === "tuned" || retainedIds.has(t.id)));

        return (
          matchesSearch &&
          matchesCompletion &&
          t.blocker >= deferredMinBlocker &&
          t.difficulty >= deferredMinDifficulty &&
          t.generality >= deferredMinGenerality
        );
      })
      .map((t) => ({
        ...t,
        score: t.blocker * 4 + (6 - t.difficulty) * 3 + t.generality * 2,
      }));
  }, [
    tasks,
    deferredSearchQuery,
    deferredMinBlocker,
    deferredMinDifficulty,
    deferredMinGenerality,
    deferredStatusFilters,
    exitingIds,
    gracePeriodIds,
    retainedIds,
  ]);

  const tasksByTier = useMemo(() => {
    const grouped: Record<number, RoadmapTask[]> = { 1: [], 2: [], 3: [], 4: [] };
    filteredTasks.forEach((t) => {
      const tier = calculateTier(t.score);
      grouped[tier].push(t);
    });
    Object.keys(grouped).forEach((k) => {
      grouped[Number(k)].sort((a, b) => b.score - a.score);
    });
    return grouped;
  }, [filteredTasks]);

  const handleCartClick = (id: string, shiftKey: boolean) => {
    if (shiftKey && lastCartClickedId) {
      const allTaskIds = filteredTasks.map((t) => t.id);
      const prevIndex = allTaskIds.indexOf(lastCartClickedId);
      const currIndex = allTaskIds.indexOf(id);
      if (prevIndex !== -1 && currIndex !== -1) {
        const start = Math.min(prevIndex, currIndex);
        const end = Math.max(prevIndex, currIndex);
        const rangeIds = allTaskIds.slice(start, end + 1);
        setCartIds((prev) => Array.from(new Set([...prev, ...rangeIds])));
        setToast({ open: true, message: `範囲内の ${rangeIds.length} 件をまとめてカートに追加しました！` });
        setLastCartClickedId(id);
        return;
      }
    }
    toggleCart(id);
    setLastCartClickedId(id);
  };

  const toggleTierCart = (tierTasks: RoadmapTask[]) => {
    const tierIds = tierTasks.map((t) => t.id);
    const allInCart = tierIds.every((id) => cartIds.includes(id));
    if (allInCart) {
      setCartIds((prev) => prev.filter((id) => !tierIds.includes(id)));
      setToast({ open: true, message: "Tier内のタスクをカートから解除しました" });
    } else {
      setCartIds((prev) => Array.from(new Set([...prev, ...tierIds])));
      setToast({ open: true, message: `Tier内の ${tierIds.length} 件をカートに追加しました！` });
    }
  };

  const copyJulesPrompt = () => {
    const jsonStr = JSON.stringify(cartIds, null, 2);
    const prompt = julesPromptTemplate.replace("{{TASK_IDS_JSON}}", jsonStr);

    navigator.clipboard
      .writeText(prompt)
      .then(() => {
        setToast({ open: true, message: `選択した ${cartIds.length} 件のJules依頼プロンプトをコピーしました！` });
      })
      .catch((err) => {
        console.error("Failed to copy prompt to clipboard:", err);
        setToast({ open: true, message: "クリップボードへのコピーに失敗しました" });
      });
  };

  const copyCartIdsOnly = () => {
    const text = cartIds.join(",");
    navigator.clipboard
      .writeText(text)
      .then(() => {
        setToast({ open: true, message: `選択した ${cartIds.length} 件のIDをコピーしました！` });
      })
      .catch((err) => {
        console.error("Failed to copy IDs to clipboard:", err);
        setToast({ open: true, message: "クリップボードへのコピーに失敗しました" });
      });
  };

  const updateNotes = (id: string, notes: string) => {
    pushHistory(tasks);
    isDirty.current = true;
    setTasks(tasks.map((t) => (t.id === id ? { ...t, notes } : t)));
  };

  return {
    tasks,
    loading,
    saveStatus,
    exitingIds,
    searchQuery,
    setSearchQuery,
    minBlocker,
    setMinBlocker,
    minDifficulty,
    setMinDifficulty,
    minGenerality,
    setMinGenerality,
    showAdvancedFilters,
    setShowAdvancedFilters,
    statusFilters,
    setStatusFilters,
    toast,
    setToast,
    cartIds,
    setCartIds,
    handleUndo,
    handleRedo,
    history,
    updateTaskStatus,
    handleCartClick,
    toggleTierCart,
    copyJulesPrompt,
    copyCartIdsOnly,
    updateNotes,
    filteredTasks,
    tasksByTier,
  };
}
