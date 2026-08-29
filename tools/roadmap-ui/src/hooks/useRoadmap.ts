import { useState, useEffect, useRef, useMemo, useDeferredValue } from "react";
import type { RoadmapTask, TaskStatus, ProgressFilter, ToastState } from "../types";
import julesPromptTemplate from "../templates/jules-prompt-template.md?raw";

export function useRoadmap() {
  const [tasks, setTasks] = useState<RoadmapTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [saveStatus, setSaveStatus] = useState<"saved" | "saving" | "error">("saved");
  const isDirty = useRef(false);

  // Selected Task for Bottom Detail Dock Panel
  const [selectedTaskId, setSelectedTaskId] = useState<string | null>(null);

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
  const [progressFilter, setProgressFilter] = useState<ProgressFilter>("incomplete");

  const [toast, setToast] = useState<ToastState>({
    open: false,
    message: "",
  });

  // Retained IDs when transitioning in filtered views
  const [retainedIds, setRetainedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    setRetainedIds(new Set());
  }, [progressFilter]);

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
          const status: TaskStatus =
            t.status === "tuned" || t.status === "implemented" ? t.status : "incomplete";
          const metrics = {
            blocker: t.metrics?.blocker ?? t.blocker ?? 3,
            difficulty: t.metrics?.difficulty ?? t.difficulty ?? 2,
            generality: t.metrics?.generality ?? t.generality ?? 3,
            score: t.metrics?.score ?? t.score ?? 30,
          };
          const details = t.details || (t.notes ? { notes: t.notes } : undefined);
          return {
            ...t,
            status,
            metrics,
            details,
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

  const pushHistory = (currentTasks: RoadmapTask[]) => {
    setHistory((prev) => ({
      past: [...prev.past.slice(-20), currentTasks],
      future: [],
    }));
  };

  const handleUndo = () => {
    if (history.past.length === 0) return;
    const previous = history.past[history.past.length - 1];
    const newPast = history.past.slice(0, history.past.length - 1);
    setHistory({
      past: newPast,
      future: [tasks, ...history.future],
    });
    setTasks(previous);
    isDirty.current = true;
    setToast({ open: true, message: "変更を取り消しました (Undo)" });
  };

  const handleRedo = () => {
    if (history.future.length === 0) return;
    const next = history.future[0];
    const newFuture = history.future.slice(1);
    setHistory({
      past: [...history.past, tasks],
      future: newFuture,
    });
    setTasks(next);
    isDirty.current = true;
    setToast({ open: true, message: "変更をやり直しました (Redo)" });
  };

  const updateNotes = (id: string, notes: string) => {
    pushHistory(tasks);
    isDirty.current = true;
    setTasks((prev) =>
      prev.map((t) =>
        t.id === id
          ? {
              ...t,
              details: {
                ...(t.details || {}),
                notes,
              },
            }
          : t
      )
    );
  };

  const updateTaskStatus = (id: string, newStatus: TaskStatus, isUndo = false) => {
    const targetTask = tasks.find((t) => t.id === id);
    if (!targetTask) return;
    const oldStatus = targetTask.status;
    if (oldStatus === newStatus) return;

    if (!isUndo) {
      pushHistory(tasks);
    }

    // Determine if task will leave current progressFilter view
    let willDisappear = false;
    if (progressFilter === "incomplete" && newStatus !== "incomplete") {
      willDisappear = true;
    } else if (progressFilter === "implemented_plus" && newStatus === "incomplete") {
      willDisappear = true;
    } else if (progressFilter === "tuned" && newStatus !== "tuned") {
      willDisappear = true;
    }

    if (willDisappear) {
      if (exitTimers.current[id]) {
        clearTimeout(exitTimers.current[id]);
      }

      setGracePeriodIds((prev) => new Set(prev).add(id));

      exitTimers.current[id] = window.setTimeout(() => {
        setExitingIds((prev) => new Set(prev).add(id));

        setTimeout(() => {
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
          delete exitTimers.current[id];
        }, 400);
      }, 2500);
    } else {
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
      setRetainedIds((prev) => {
        const next = new Set(prev);
        next.add(id);
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
            }
          : t
      )
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
  const deferredProgressFilter = useDeferredValue(progressFilter);
  const deferredMinBlocker = useDeferredValue(minBlocker);
  const deferredMinDifficulty = useDeferredValue(minDifficulty);
  const deferredMinGenerality = useDeferredValue(minGenerality);

  const filteredTasks = useMemo(() => {
    return tasks
      .filter((t) => {
        const query = deferredSearchQuery.trim().toLowerCase();
        let matchesSearch = true;

        if (query) {
          const notesText = t.details?.notes || "";
          const normalizedId = t.id.toLowerCase().replace(/[\s\-_]/g, "");
          const normalizedTitle = t.title.toLowerCase().replace(/[\s\-_]/g, "");
          const normalizedNotes = notesText.toLowerCase().replace(/[\s\-_]/g, "");

          if (query.includes(",")) {
            const subQueries = query.split(",").map((s) => s.trim()).filter(Boolean);
            matchesSearch = subQueries.some((sq) => {
              const normSub = sq.replace(/[\s\-_]/g, "");
              return (
                t.id.toLowerCase().includes(sq) ||
                normalizedId.includes(normSub) ||
                t.title.toLowerCase().includes(sq) ||
                normalizedTitle.includes(normSub) ||
                notesText.toLowerCase().includes(sq) ||
                normalizedNotes.includes(normSub)
              );
            });
          } else {
            const tokens = query.split(/\s+/).filter(Boolean);
            matchesSearch = tokens.every((token) => {
              const normToken = token.replace(/[\s\-_]/g, "");
              return (
                t.id.toLowerCase().includes(token) ||
                normalizedId.includes(normToken) ||
                t.title.toLowerCase().includes(token) ||
                normalizedTitle.includes(normToken) ||
                notesText.toLowerCase().includes(token) ||
                normalizedNotes.includes(normToken)
              );
            });
          }
        }

        // If user is actively searching, show all search matches across all statuses and metrics
        if (query) {
          return matchesSearch;
        }

        const taskStatus = t.status || "incomplete";
        const isExiting = exitingIds.has(t.id);

        // Progress-based Filtering (Pattern A)
        let matchesProgress = false;
        if (isExiting || gracePeriodIds.has(t.id) || retainedIds.has(t.id)) {
          matchesProgress = true;
        } else if (deferredProgressFilter === "all") {
          matchesProgress = true;
        } else if (deferredProgressFilter === "incomplete") {
          matchesProgress = taskStatus === "incomplete";
        } else if (deferredProgressFilter === "implemented_plus") {
          matchesProgress = taskStatus === "implemented" || taskStatus === "tuned";
        } else if (deferredProgressFilter === "tuned") {
          matchesProgress = taskStatus === "tuned";
        }

        const matchesBlocker = t.metrics.blocker >= deferredMinBlocker;
        const matchesDifficulty = t.metrics.difficulty >= deferredMinDifficulty;
        const matchesGenerality = t.metrics.generality >= deferredMinGenerality;

        return (
          matchesProgress &&
          matchesBlocker &&
          matchesDifficulty &&
          matchesGenerality
        );
      })
      .sort((a, b) => b.metrics.score - a.metrics.score);
  }, [
    tasks,
    deferredSearchQuery,
    deferredProgressFilter,
    deferredMinBlocker,
    deferredMinDifficulty,
    deferredMinGenerality,
    gracePeriodIds,
    exitingIds,
    retainedIds,
  ]);

  const tasksByTier = useMemo(() => {
    const grouped: Record<number, RoadmapTask[]> = { 1: [], 2: [], 3: [], 4: [] };
    filteredTasks.forEach((t) => {
      const tier = calculateTier(t.metrics.score);
      grouped[tier].push(t);
    });
    return grouped;
  }, [filteredTasks]);

  const toggleTierCart = (tierTasks: RoadmapTask[]) => {
    const tierIds = tierTasks.map((t) => t.id);
    const allInCart = tierIds.every((id) => cartIds.includes(id));
    if (allInCart) {
      setCartIds((prev) => prev.filter((id) => !tierIds.includes(id)));
    } else {
      setCartIds((prev) => Array.from(new Set([...prev, ...tierIds])));
    }
  };

  const handleCartClick = (id: string, shiftKey: boolean) => {
    setSelectedTaskId(id);
    const isSelected = cartIds.includes(id);

    if (shiftKey && lastCartClickedId && lastCartClickedId !== id) {
      const allFilteredIds = filteredTasks.map((t) => t.id);
      const startIndex = allFilteredIds.indexOf(lastCartClickedId);
      const endIndex = allFilteredIds.indexOf(id);

      if (startIndex !== -1 && endIndex !== -1) {
        const [low, high] = [Math.min(startIndex, endIndex), Math.max(startIndex, endIndex)];
        const rangeIds = allFilteredIds.slice(low, high + 1);

        if (isSelected) {
          setCartIds((prev) => prev.filter((i) => !rangeIds.includes(i)));
        } else {
          setCartIds((prev) => Array.from(new Set([...prev, ...rangeIds])));
        }
        setLastCartClickedId(id);
        return;
      }
    }

    toggleCart(id);
    setLastCartClickedId(id);
  };

  const copyJulesPrompt = () => {
    if (cartIds.length === 0) return;
    const selectedTasks = tasks.filter((t) => cartIds.includes(t.id));
    const taskDetails = selectedTasks
      .map((t) => {
        const details = t.details || {};
        return `### 【${t.id}】${t.title} (Domain: ${t.domain}, Category: ${t.category || "N/A"})
- スコア: ${t.metrics.score} (Blocker: ${t.metrics.blocker}, Diff: ${t.metrics.difficulty}, Gen: ${t.metrics.generality})
- C# 記述例:
\`\`\`csharp
${details.csharp_example || "// N/A"}
\`\`\`
- 現状の課題: ${details.current_behavior || "N/A"}
- 対策方針 / CUDA変換先: ${details.target_cuda || details.strategy || "N/A"}
- メモ: ${details.notes || "なし"}`;
      })
      .join("\n\n");

    const promptText = julesPromptTemplate.replace("{{TASK_DETAILS}}", taskDetails);
    navigator.clipboard.writeText(promptText);
    setToast({ open: true, message: `Jules用プロンプトをクリップボードにコピーしました (${cartIds.length}件)` });
  };

  const copyCartIdsOnly = () => {
    if (cartIds.length === 0) return;
    const idList = cartIds.join(", ");
    navigator.clipboard.writeText(idList);
    setToast({ open: true, message: `タスクID一覧をコピーしました (${cartIds.length}件)` });
  };

  return {
    tasks,
    loading,
    saveStatus,
    selectedTaskId,
    setSelectedTaskId,
    history,
    handleUndo,
    handleRedo,
    searchQuery,
    setSearchQuery,
    progressFilter,
    setProgressFilter,
    showAdvancedFilters,
    setShowAdvancedFilters,
    minBlocker,
    setMinBlocker,
    minDifficulty,
    setMinDifficulty,
    minGenerality,
    setMinGenerality,
    toast,
    setToast,
    cartIds,
    setCartIds,
    handleCartClick,
    updateTaskStatus,
    updateNotes,
    toggleTierCart,
    copyJulesPrompt,
    copyCartIdsOnly,
    filteredTasks,
    tasksByTier,
    exitingIds,
  };
}
