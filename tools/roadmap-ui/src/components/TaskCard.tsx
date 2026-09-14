import React from 'react';
import { Card, CardContent, Box, Chip, IconButton, Typography } from '@mui/material';
import { CheckCircle as CheckCircleIcon, RadioButtonUnchecked as UncheckedIcon } from '@mui/icons-material';
import { DOMAIN_LABELS } from '../types';
import type { RoadmapTask, TaskStatus } from '../types';

interface TaskCardProps {
  task: RoadmapTask;
  inCart: boolean;
  isSelected?: boolean;
  onCartClick: (id: string, shiftKey: boolean) => void;
  onStatusChange: (id: string, newStatus: TaskStatus) => void;
  onSelectTask?: (id: string) => void;
}

const STATUS_CHIP_OPTIONS: { val: TaskStatus; label: string; color: string; bg: string; border: string }[] = [
  { val: 'incomplete', label: '未完', color: 'text.secondary', bg: 'rgba(255, 255, 255, 0.05)', border: '#49454f' },
  { val: 'planned', label: '📅 計画', color: '#90caf9', bg: '#0d294a', border: '#1e4976' },
  { val: 'implemented', label: '🛠️ 実装', color: '#c4eed0', bg: '#1a2e22', border: '#386a4f' },
  { val: 'tuned', label: '⚡ パフォ', color: '#f3e5f5', bg: '#331b3f', border: '#ab47bc' },
  { val: 'rejected', label: '❌ 却下', color: '#ffcdd2', bg: '#3e1a1d', border: '#ef5350' },
];

export const TaskCard: React.FC<TaskCardProps> = React.memo(({
  task,
  inCart,
  isSelected,
  onCartClick,
  onStatusChange,
  onSelectTask,
}) => {
  const currentStatus = task.status || 'incomplete';
  const isImplemented = currentStatus === 'implemented';
  const isTuned = currentStatus === 'tuned';
  const isRejected = currentStatus === 'rejected';
  const metrics = task.metrics || { blocker: 3, difficulty: 2, generality: 3, score: 30 };

  const handleCardClick = () => {
    onSelectTask?.(task.id);
  };

  return (
    <Card
      onClick={handleCardClick}
      sx={{
        height: '100%',
        overflow: 'hidden',
        display: 'flex',
        flexDirection: 'column',
        bgcolor: isSelected
          ? 'rgba(45, 35, 65, 0.9)'
          : inCart
          ? '#262136'
          : isRejected
          ? '#241416'
          : isTuned
          ? '#281b33'
          : isImplemented
          ? '#1f2b24'
          : 'background.paper',
        borderColor: isSelected
          ? '#d0bcff'
          : inCart
          ? 'primary.main'
          : isRejected
          ? '#e57373'
          : isTuned
          ? '#ab47bc'
          : isImplemented
          ? '#386a4f'
          : 'divider',
        borderWidth: isSelected || inCart ? 2 : 1,
        position: 'relative',
        cursor: 'pointer',
        transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
        transform: isSelected || inCart ? 'translateY(-2px)' : 'none',
        boxShadow: isSelected
          ? '0 0 16px rgba(208, 188, 255, 0.35)'
          : inCart
          ? '0 4px 12px rgba(208, 188, 255, 0.15)'
          : 'none',
        '&:hover': {
          borderColor: isSelected ? '#d0bcff' : 'primary.light',
        },
      }}
    >
      <CardContent sx={{ p: 2, flex: '1 0 auto', display: 'flex', flexDirection: 'column', gap: 1 }}>
        {/* Header Tags Row */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 0.5 }}>
          <Box sx={{ display: 'flex', gap: 0.6, alignItems: 'center', flexWrap: 'wrap', flex: 1, minWidth: 0 }}>
            <Chip
              label={task.id}
              size="small"
              sx={{
                bgcolor: 'primary.dark',
                color: 'primary.light',
                fontWeight: 700,
                fontSize: '0.75rem',
                height: 22,
                flexShrink: 0,
              }}
            />
            <Chip
              label={task.domain}
              size="small"
              variant="outlined"
              title={DOMAIN_LABELS[task.domain] || task.domain}
              sx={{
                fontSize: '0.7rem',
                color: 'text.secondary',
                borderColor: '#49454f',
                height: 22,
                flexShrink: 0,
              }}
            />
            {task.category && (
              <Chip
                label={task.category}
                size="small"
                variant="outlined"
                title={task.category}
                sx={{
                  fontSize: '0.68rem',
                  color: '#d0bcff',
                  borderColor: 'rgba(208, 188, 255, 0.3)',
                  height: 22,
                  maxWidth: 130,
                  '& .MuiChip-label': {
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    px: 0.8,
                  },
                }}
              />
            )}
            {task.pr && task.pr.map((prNum) => (
              <Chip
                key={prNum}
                label={`#${prNum}`}
                size="small"
                variant="outlined"
                onClick={(e) => {
                  e.stopPropagation();
                  window.open(`https://github.com/kassyi/Henicos/pull/${prNum}`, '_blank');
                }}
                title={`PR #${prNum}`}
                sx={{
                  fontSize: '0.68rem',
                  color: '#81c784',
                  borderColor: 'rgba(129, 199, 132, 0.3)',
                  height: 22,
                  cursor: 'pointer',
                  '&:hover': { bgcolor: 'rgba(129, 199, 132, 0.12)' },
                }}
              />
            ))}
          </Box>

          <IconButton
            size="small"
            onClick={(e) => {
              e.stopPropagation();
              onCartClick(task.id, e.shiftKey);
            }}
            title={inCart ? 'カートから削除 (Shift+Clickで範囲選択)' : 'カートに追加 (Shift+Clickで範囲選択)'}
            sx={{
              color: inCart ? 'primary.light' : 'text.disabled',
              flexShrink: 0,
              p: 0.5,
              '&:hover': { color: 'primary.main' },
            }}
          >
            {inCart ? <CheckCircleIcon fontSize="small" /> : <UncheckedIcon fontSize="small" />}
          </IconButton>
        </Box>

        {/* Task Title */}
        <Typography
          variant="subtitle1"
          sx={{
            fontWeight: 600,
            fontSize: '0.92rem',
            color: isRejected ? '#ef9a9a' : isTuned ? '#f3e5f5' : isImplemented ? '#c4eed0' : 'text.primary',
            textDecoration: currentStatus !== 'incomplete' ? 'line-through' : 'none',
            lineHeight: 1.35,
            mt: 0.5,
            mb: 0.5,
            wordBreak: 'break-word',
          }}
        >
          {task.title}
        </Typography>

        {task.details?.memo && (
          <Typography
            variant="caption"
            sx={{
              color: '#ef9a9a',
              bgcolor: 'rgba(239, 83, 80, 0.08)',
              border: '1px solid rgba(239, 83, 80, 0.2)',
              p: 0.5,
              borderRadius: 1,
              fontSize: '0.7rem',
              display: 'block',
              lineHeight: 1.25,
            }}
          >
            💬 {task.details.memo}
          </Typography>
        )}

        {/* Metrics Score & Status Chips Row */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 0.8, mt: 'auto', pt: 1 }}>
          <Typography variant="caption" sx={{ color: 'text.disabled', fontSize: '0.72rem', whiteSpace: 'nowrap' }}>
            Score: <span style={{ color: '#d0bcff', fontWeight: 700 }}>{metrics.score.toFixed(1)}</span> (B:{metrics.blocker} D:{metrics.difficulty} G:{metrics.generality})
          </Typography>

          <Box sx={{ display: 'flex', gap: 0.4, flexShrink: 0 }}>
            {STATUS_CHIP_OPTIONS.map((s) => (
              <Chip
                key={s.val}
                label={s.label}
                size="small"
                onClick={(e) => {
                  e.stopPropagation();
                  onStatusChange(task.id, s.val);
                }}
                sx={{
                  bgcolor: currentStatus === s.val ? s.bg : 'transparent',
                  color: currentStatus === s.val ? s.color : 'text.disabled',
                  border: `1px solid ${currentStatus === s.val ? s.border : 'transparent'}`,
                  fontWeight: currentStatus === s.val ? 700 : 400,
                  fontSize: '0.68rem',
                  height: 20,
                  px: 0.4,
                  '& .MuiChip-label': { px: 0.5 },
                  cursor: 'pointer',
                  opacity: currentStatus === s.val ? 1 : 0.6,
                  '&:hover': { opacity: 1, bgcolor: s.bg, color: s.color },
                }}
              />
            ))}
          </Box>
        </Box>
      </CardContent>
    </Card>
  );
});
