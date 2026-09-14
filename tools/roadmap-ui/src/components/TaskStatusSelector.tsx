import React from 'react';
import { Box, Typography, Chip } from '@mui/material';
import type { TaskStatus } from '../types';

interface TaskStatusSelectorProps {
  taskId: string;
  currentStatus: TaskStatus;
  onStatusChange: (id: string, newStatus: TaskStatus) => void;
  isVertical?: boolean;
}

const STATUS_CHIP_OPTIONS: { val: TaskStatus; label: string; color: string; bg: string; border: string }[] = [
  { val: 'incomplete', label: '未完', color: '#9e9e9e', bg: 'rgba(255, 255, 255, 0.04)', border: 'rgba(255, 255, 255, 0.12)' },
  { val: 'planned', label: '📅 計画', color: '#64b5f6', bg: 'rgba(100, 181, 246, 0.12)', border: 'rgba(100, 181, 246, 0.3)' },
  { val: 'implemented', label: '🛠️ 実装', color: '#81c784', bg: 'rgba(129, 199, 132, 0.12)', border: 'rgba(129, 199, 132, 0.3)' },
  { val: 'tuned', label: '⚡ パフォ', color: '#ce93d8', bg: 'rgba(206, 147, 216, 0.12)', border: 'rgba(206, 147, 216, 0.3)' },
  { val: 'rejected', label: '❌ 却下', color: '#ef9a9a', bg: 'rgba(239, 83, 80, 0.12)', border: 'rgba(239, 83, 80, 0.3)' },
];

export const TaskStatusSelector: React.FC<TaskStatusSelectorProps> = ({ taskId, currentStatus, onStatusChange, isVertical }) => {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', ...(isVertical ? { bgcolor: 'rgba(255, 255, 255, 0.02)', p: 1, borderRadius: 2 } : { flexWrap: 'wrap', gap: 1.5 }) }}>
      {isVertical && (
        <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, fontSize: '0.75rem', pl: 0.5 }}>
          進捗ステータス:
        </Typography>
      )}
      <Box sx={{ display: 'flex', gap: 0.6 }}>
        {STATUS_CHIP_OPTIONS.map((s) => (
          <Chip
            key={s.val}
            label={s.label}
            size="small"
            onClick={() => onStatusChange(taskId, s.val)}
            sx={{
              bgcolor: currentStatus === s.val ? s.bg : 'transparent',
              color: currentStatus === s.val ? s.color : 'text.disabled',
              border: `1px solid ${currentStatus === s.val ? s.border : 'transparent'}`,
              fontWeight: currentStatus === s.val ? 700 : 400,
              fontSize: '0.72rem',
              height: 24,
              cursor: 'pointer',
              opacity: currentStatus === s.val ? 1 : 0.6,
              '&:hover': { opacity: 1, bgcolor: s.bg },
            }}
          />
        ))}
      </Box>
    </Box>
  );
};
