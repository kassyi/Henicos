export type TaskStatus = 'incomplete' | 'implemented' | 'tuned';

export interface RoadmapTask {
  id: string;
  title: string;
  domain: string;
  blocker: number;
  difficulty: number;
  generality: number;
  score: number;
  completed?: boolean;
  status?: TaskStatus;
  notes: string;
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
