export type TaskStatus = 'incomplete' | 'implemented' | 'tuned';

export type ProgressFilter = 'incomplete' | 'implemented_plus' | 'tuned' | 'all';

export interface TaskMetrics {
  blocker: number;
  difficulty: number;
  generality: number;
  score: number;
}

export interface TaskDetails {
  csharp_example?: string;
  current_behavior?: string;
  target_cuda?: string;
  strategy?: string;
  notes?: string;
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
  GBI: 'GBI (GPU基盤)',
  GSH: 'GSH (トランスパイラ)',
  GEN: 'GEN (ジェネレータ基盤)',
};
