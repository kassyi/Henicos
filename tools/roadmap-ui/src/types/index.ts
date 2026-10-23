export type TaskStatus = 'incomplete' | 'planned' | 'implemented' | 'tuned' | 'rejected';

export type ProgressFilter = 'incomplete' | 'planned' | 'implemented_plus' | 'tuned' | 'rejected' | 'all';

export type DockPosition = 'bottom' | 'left' | 'right';

export interface TaskMetrics {
  blocker: number;
  difficulty: number;
  generality: number;
  score: number;
}

export interface TaskDetails {
  csharp_example?: string;
  cpp_example?: string;
  current_behavior?: string;
  target_cuda?: string;
  strategy?: string;
  notes?: string;
  memo?: string;
}

export interface RoadmapTask {
  id: string;
  title: string;
  domain: string;
  category?: string;
  status: TaskStatus;
  metrics: TaskMetrics;
  details?: TaskDetails;
  files?: string[];
  pr?: number[];
  _searchableText?: string;
}

export interface ToastState {
  open: boolean;
  message: string;
  actionText?: string;
  onAction?: () => void;
  severity?: 'success' | 'info' | 'warning' | 'error';
}

export const DOMAIN_LABELS: Record<string, string> = {
  SYN: 'SYN (構文)',
  GSH: 'GSH (トランスパイラ)',
  GEN: 'GEN (ジェネレータ基盤)',
  GBI: 'GBI (GpuBigInteger)',
  GBF: 'GBF (GpuBigFloat)',
  BCL: 'BCL (基本クラスライブラリ)',
  MEM: 'MEM (メモリ管理)',
};
