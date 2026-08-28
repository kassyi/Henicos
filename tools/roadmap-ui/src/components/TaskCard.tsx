import React from 'react';
import { Card, CardContent, Box, Chip, IconButton, Typography, TextField } from '@mui/material';
import { CheckCircle as CheckCircleIcon, RadioButtonUnchecked as UncheckedIcon } from '@mui/icons-material';
import { DOMAIN_LABELS } from '../types';
import type { RoadmapTask, TaskStatus } from '../types';

interface TaskCardProps {
  task: RoadmapTask;
  inCart: boolean;
  isExiting: boolean;
  onCartClick: (id: string, shiftKey: boolean) => void;
  onStatusChange: (id: string, newStatus: TaskStatus) => void;
  onNotesChange: (id: string, notes: string) => void;
}

const STATUS_CHIP_OPTIONS: { val: TaskStatus; label: string; color: string; bg: string; border: string }[] = [
  { val: 'incomplete', label: '未完', color: 'text.secondary', bg: 'rgba(255, 255, 255, 0.05)', border: '#49454f' },
  { val: 'implemented', label: '🛠️ 実装', color: '#c4eed0', bg: '#1a2e22', border: '#386a4f' },
  { val: 'tuned', label: '⚡ パフォ', color: '#f3e5f5', bg: '#331b3f', border: '#ab47bc' },
];

export const TaskCard: React.FC<TaskCardProps> = React.memo(({
  task,
  inCart,
  isExiting,
  onCartClick,
  onStatusChange,
  onNotesChange,
}) => {
  const currentStatus = task.status || (task.completed ? 'implemented' : 'incomplete');
  const isImplemented = currentStatus === 'implemented';
  const isTuned = currentStatus === 'tuned';

  return (
    <Card
      onClick={(e) => onCartClick(task.id, e.shiftKey)}
      sx={{
        height: isExiting ? 0 : '100%',
        maxHeight: isExiting ? 0 : 600,
        overflow: 'hidden',
        display: 'flex',
        flexDirection: 'column',
        bgcolor: inCart ? '#262136' : isTuned ? '#281b33' : isImplemented ? '#1f2b24' : 'background.paper',
        borderColor: inCart ? 'primary.main' : isTuned ? '#ab47bc' : isImplemented ? '#386a4f' : 'divider',
        borderWidth: inCart ? 2 : 1,
        position: 'relative',
        cursor: 'pointer',
        transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
        transform: isExiting ? 'translateX(120px) scale(0.92)' : inCart ? 'translateY(-2px)' : 'none',
        opacity: isExiting ? 0 : 1,
        pointerEvents: isExiting ? 'none' : 'auto',
        my: isExiting ? 0 : undefined,
        boxShadow: inCart ? '0 4px 12px rgba(208, 188, 255, 0.15)' : 'none',
        '&:hover': {
          borderColor: inCart ? 'primary.light' : 'text.disabled',
          '& .hover-actions': { opacity: 1, pointerEvents: 'auto' },
        },
      }}
    >
      <CardContent sx={{ p: 2, flex: '1 0 auto', display: 'flex', flexDirection: 'column', gap: 1 }}>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 0.5 }}>
          <Box sx={{ display: 'flex', gap: 0.6, alignItems: 'center', flexWrap: 'wrap' }}>
            <Chip
              label={task.id}
              size="small"
              sx={{ bgcolor: 'primary.dark', color: 'primary.light', fontWeight: 700, fontSize: '0.72rem', height: 22 }}
            />
            <Chip
              label={task.domain}
              size="small"
              variant="outlined"
              title={DOMAIN_LABELS[task.domain] || task.domain}
              sx={{ fontSize: '0.68rem', color: 'text.secondary', borderColor: '#49454f', height: 22 }}
            />

            <Box
              className="hover-actions"
              sx={{
                display: 'flex',
                gap: 0.5,
                opacity: 0,
                pointerEvents: 'none',
                transition: 'opacity 0.2s',
                '@media (hover: none)': { opacity: 1, pointerEvents: 'auto' },
              }}
            >
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
                    border: `1px solid ${currentStatus === s.val ? s.border : '#332d41'}`,
                    fontWeight: currentStatus === s.val ? 700 : 500,
                    fontSize: '0.65rem',
                    height: 22,
                    cursor: 'pointer',
                    opacity: currentStatus === s.val ? 1 : 0.6,
                    transition: 'all 0.2s',
                    '&:hover': { opacity: 1, bgcolor: s.bg, color: s.color, borderColor: s.border },
                  }}
                />
              ))}
            </Box>
          </Box>

          <IconButton
            className={inCart ? '' : 'hover-actions'}
            size="small"
            sx={{
              p: 0,
              ml: 1,
              opacity: inCart ? 1 : 0,
              pointerEvents: inCart ? 'auto' : 'none',
              transition: 'opacity 0.2s',
              '@media (hover: none)': { opacity: 1, pointerEvents: 'auto' },
              color: inCart ? 'primary.light' : 'text.disabled',
              '&:hover': { color: 'primary.main' },
            }}
          >
            {inCart ? <CheckCircleIcon fontSize="small" /> : <UncheckedIcon fontSize="small" />}
          </IconButton>
        </Box>

        <Typography
          variant="subtitle1"
          sx={{
            fontWeight: 600,
            fontSize: '0.95rem',
            color: isTuned ? '#f3e5f5' : isImplemented ? '#c4eed0' : 'text.primary',
            textDecoration: currentStatus !== 'incomplete' ? 'line-through' : 'none',
            lineHeight: 1.3,
            mt: 0.5,
            mb: 0.5,
          }}
        >
          {task.title}
        </Typography>

        <Typography variant="caption" sx={{ color: 'text.disabled', fontSize: '0.7rem' }}>
          Score: <span style={{ color: '#d0bcff', fontWeight: 700 }}>{task.score.toFixed(1)}</span> (B:{task.blocker} D:{task.difficulty} G:{task.generality})
        </Typography>

        <TextField
          multiline
          variant="standard"
          placeholder="📝 メモを追加 (クリックで編集)..."
          value={task.notes}
          onClick={(e) => e.stopPropagation()}
          onChange={(e) => onNotesChange(task.id, e.target.value)}
          sx={{
            mt: 'auto',
            pt: 1,
            borderRadius: 1,
            transition: 'background-color 0.2s',
            '&:hover, &:focus-within': { bgcolor: 'rgba(255,255,255,0.03)', px: 0.5 },
            '& .MuiInputBase-input': { fontSize: '0.78rem', color: 'text.secondary', lineHeight: 1.4 },
            '& .MuiInput-underline:before': { borderBottom: 'none' },
            '& .MuiInput-underline:hover:not(.Mui-disabled):before': { borderBottom: 'none' },
            '& .MuiInput-underline:after': { borderBottom: 'none' },
          }}
        />
      </CardContent>
    </Card>
  );
});
