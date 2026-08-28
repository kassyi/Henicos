import { useState, useEffect, useRef, useMemo } from 'react';
import type { RoadmapTask, TaskStatus, ToastState } from '../types';

export function useRoadmap() {
  const [tasks, setTasks] = useState<RoadmapTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [saveStatus, setSaveStatus] = useState<'saved' | 'saving' | 'error'>('saved');
  const isInitialLoaded = useRef(false);

  // Undo / Redo History
  const [history, setHistory] = useState<{ past: RoadmapTask[][]; future: RoadmapTask[][] }>({
    past: [],
    future: [],
  });

  // Google Keep-like Slide Out Animation & Grace Period
  const [gracePeriodIds, setGracePeriodIds] = useState<Set<string>>(new Set());
  const [exitingIds, setExitingIds] = useState<Set<string>>(new Set());
  const exitTimers = useRef<Record<string, number>>({});

  const [searchQuery, setSearchQuery] = useState('');
  const [minBlocker, setMinBlocker] = useState<number>(1);
  const [minDifficulty, setMinDifficulty] = useState<number>(1);
  const [minGenerality, setMinGenerality] = useState<number>(1);
  const [showAdvancedFilters, setShowAdvancedFilters] = useState(true);
  const [completionFilter, setCompletionFilter] = useState<'all' | 'incomplete' | 'implemented' | 'tuned' | 'completed'>('incomplete');
  
  const [toast, setToast] = useState<ToastState>({
    open: false,
    message: '',
  });
  
  // AI Cart Feature
  const [cartIds, setCartIds] = useState<string[]>([]);
  const [lastCartClickedId, setLastCartClickedId] = useState<string | null>(null);

  useEffect(() => {
    fetch('/api/load')
      .then((res) => {
        if (!res.ok) throw new Error('Data not found');
        return res.json();
      })
      .then((data: any[]) => {
        const normalized: RoadmapTask[] = data.map((t) => {
          let status: TaskStatus = 'incomplete';
          if (t.status === 'tuned' || t.status === 'implemented' || t.status === 'incomplete') {
            status = t.status;
          } else if (t.completed) {
            status = 'implemented';
          }
          return {
            ...t,
            status,
            completed: status !== 'incomplete',
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
    if (loading) return;
    if (!isInitialLoaded.current) {
      isInitialLoaded.current = true;
      return;
    }

    setSaveStatus('saving');
    const timer = setTimeout(async () => {
      try {
        const res = await fetch('/api/save', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(tasks),
        });
        if (res.ok) {
          setSaveStatus('saved');
        } else {
          setSaveStatus('error');
        }
      } catch (err) {
        console.error(err);
        setSaveStatus('error');
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
      setTasks(previous);
      setToast({
        open: true,
        message: '直前の操作を元に戻しました (Undo)',
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
      setTasks(next);
      setToast({
        open: true,
        message: '操作をやり直しました (Redo)',
      });
      return { past: newPast, future: newFuture };
    });
  };

  // Keyboard Shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') {
        return;
      }

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        if (e.shiftKey) {
          e.preventDefault();
          handleRedo();
        } else {
          e.preventDefault();
          handleUndo();
        }
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') {
        e.preventDefault();
        handleRedo();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [tasks, history]);

  const updateTaskStatus = (id: string, newStatus: TaskStatus, recordHistory = true) => {
    const targetTask = tasks.find((t) => t.id === id);
    if (!targetTask) return;
    const oldStatus = targetTask.status || (targetTask.completed ? 'implemented' : 'incomplete');
    if (oldStatus === newStatus) return;

    if (recordHistory) {
      pushHistory(tasks);
    }

    const willBeFilteredOut =
      (completionFilter === 'incomplete' && newStatus !== 'incomplete') ||
      (completionFilter === 'implemented' && newStatus !== 'implemented') ||
      (completionFilter === 'tuned' && newStatus !== 'tuned') ||
      (completionFilter === 'completed' && newStatus === 'incomplete');

    if (willBeFilteredOut) {
      setGracePeriodIds(prev => new Set(prev).add(id));
      
      if (exitTimers.current[id]) clearTimeout(exitTimers.current[id]);
      
      exitTimers.current[id] = setTimeout(() => {
        setGracePeriodIds(prev => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
        setExitingIds(prev => new Set(prev).add(id));
        setTimeout(() => {
          setExitingIds(prev => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
        }, 350);
      }, 4000); // 4 seconds grace period
    } else {
      if (exitTimers.current[id]) {
        clearTimeout(exitTimers.current[id]);
        delete exitTimers.current[id];
      }
      setGracePeriodIds(prev => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
      setExitingIds(prev => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
    }

    setTasks((prev) =>
      prev.map((t) =>
        t.id === id
          ? {
              ...t,
              status: newStatus,
              completed: newStatus !== 'incomplete',
            }
          : t
      )
    );

    const statusLabel =
      newStatus === 'implemented' ? '🛠️ 実装完了' : newStatus === 'tuned' ? '⚡ パフォ完了' : '未完了';

    setToast({
      open: true,
      message: `「${targetTask.id}」を ${statusLabel} にしました`,
      actionText: '元に戻す',
      onAction: () => {
        updateTaskStatus(id, oldStatus, true);
      },
    });
  };

  const toggleCart = (id: string) => {
    setCartIds((prev) =>
      prev.includes(id) ? prev.filter((i) => i !== id) : [...prev, id]
    );
  };

  const calculateTier = (score: number) => {
    if (score >= 36) return 1;
    if (score >= 28) return 2;
    if (score >= 20) return 3;
    return 4;
  };

  const filteredTasks = useMemo(() => {
    return tasks.filter((t) => {
      const matchesSearch = t.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
        t.id.toLowerCase().includes(searchQuery.toLowerCase()) ||
        t.notes.toLowerCase().includes(searchQuery.toLowerCase());
      
      const taskStatus = t.status || (t.completed ? 'implemented' : 'incomplete');
      const isExiting = exitingIds.has(t.id);
      
      const matchesCompletion = 
        isExiting ||
        gracePeriodIds.has(t.id) ||
        completionFilter === 'all' || 
        (completionFilter === 'incomplete' && taskStatus === 'incomplete') ||
        (completionFilter === 'implemented' && taskStatus === 'implemented') ||
        (completionFilter === 'tuned' && taskStatus === 'tuned') ||
        (completionFilter === 'completed' && taskStatus !== 'incomplete');

      return matchesSearch &&
        matchesCompletion &&
        t.blocker >= minBlocker &&
        t.difficulty >= minDifficulty &&
        t.generality >= minGenerality;
    });
  }, [tasks, searchQuery, minBlocker, minDifficulty, minGenerality, completionFilter, exitingIds, gracePeriodIds]);

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
      setToast({ open: true, message: 'Tier内のタスクをカートから解除しました' });
    } else {
      setCartIds((prev) => Array.from(new Set([...prev, ...tierIds])));
      setToast({ open: true, message: `Tier内の ${tierIds.length} 件をカートに追加しました！` });
    }
  };

  const copyVibeCodingPrompt = () => {
    const jsonStr = JSON.stringify(cartIds, null, 2);
    const prompt = `【テスト先行AIバイブコーディング】
以下のタスクを対象に、「テストコード先行生成（Golden Master / Snapshot Testの作成）」のフェーズ1を開始してください。

ASTの責務領域や対象ファイルに基づいて、干渉しないブランチ（レーン）を切って並行実装を進めるアプローチを採用します。
エージェント自身の判断でタスク間の依存関係やコードの干渉度合いを分析し、以下の要領で事前のレーン分けを提案してください：
- 実装が干渉し合うタスク（例: 構文解析とそれを利用する処理など）は「同じレーン（例: レーン1）」にまとめる
- 完全に独立して実装可能なタスクは「別のレーン（例: レーン2）」に分ける

対象タスクID:
${jsonStr}

指示:
1. \`AGENTS.md\` および \`.agents/skills/\` 以下のガイドライン（qa-testing-guidelines, gsharp-transpiler-guidelines）を熟読・参照してください。
2. まず初めに、上記に基づいたレーン分けの計画を提示してください。
3. その後、レーンごとにQA品質基準（異常系の例外スローなど）を満たす Snapshot Test 用のテストコードを設計し、実装計画を提案してから実際の実装に進んでください。`;
    
    navigator.clipboard.writeText(prompt);
    setToast({ open: true, message: `選択した ${cartIds.length} 件のVibe Codingプロンプトをコピーしました！` });
  };

  const updateNotes = (id: string, notes: string) => {
    pushHistory(tasks);
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
    completionFilter,
    setCompletionFilter,
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
    copyVibeCodingPrompt,
    updateNotes,
    filteredTasks,
    tasksByTier,
  };
}
