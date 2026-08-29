import React, { useState, useEffect, useRef } from 'react';
import {
  Paper,
  Box,
  Typography,
  Chip,
  IconButton,
  TextField,
  Collapse,
} from '@mui/material';
import {
  KeyboardArrowDown as CollapseIcon,
  KeyboardArrowUp as ExpandIcon,
  Code as CodeIcon,
  ReportProblemOutlined as WarningIcon,
  CheckCircleOutlined as CheckIcon,
  EditNote as NoteIcon,
  TouchAppOutlined as TouchAppIcon,
} from '@mui/icons-material';
import { DOMAIN_LABELS } from '../types';
import type { RoadmapTask, TaskStatus } from '../types';
import { CsCodeHighlighter } from './CsCodeHighlighter';

interface TaskDetailPanelProps {
  task: RoadmapTask | null;
  onStatusChange: (id: string, newStatus: TaskStatus) => void;
  onNotesChange: (id: string, notes: string) => void;
  onHeightChange?: (height: number) => void;
}

const STATUS_CHIP_OPTIONS: { val: TaskStatus; label: string; color: string; bg: string; border: string }[] = [
  { val: 'incomplete', label: '未完', color: '#9e9e9e', bg: 'rgba(255, 255, 255, 0.04)', border: 'rgba(255, 255, 255, 0.12)' },
  { val: 'implemented', label: '🛠️ 実装', color: '#81c784', bg: 'rgba(129, 199, 132, 0.12)', border: 'rgba(129, 199, 132, 0.3)' },
  { val: 'tuned', label: '⚡ パフォ', color: '#ce93d8', bg: 'rgba(206, 147, 216, 0.12)', border: 'rgba(206, 147, 216, 0.3)' },
];

const getTierInfo = (score: number) => {
  if (score >= 36) return { label: 'Tier 1 (最優先)', color: '#ffd54f', bg: 'rgba(255, 213, 79, 0.12)', border: 'rgba(255, 213, 79, 0.4)' };
  if (score >= 28) return { label: 'Tier 2 (優先)', color: '#b388ff', bg: 'rgba(179, 136, 255, 0.12)', border: 'rgba(179, 136, 255, 0.4)' };
  if (score >= 20) return { label: 'Tier 3 (通常)', color: '#80d8ff', bg: 'rgba(128, 216, 255, 0.12)', border: 'rgba(128, 216, 255, 0.4)' };
  return { label: 'Tier 4 (後回し)', color: '#9e9e9e', bg: 'rgba(158, 158, 158, 0.12)', border: 'rgba(158, 158, 158, 0.3)' };
};

export const TaskDetailPanel: React.FC<TaskDetailPanelProps> = ({
  task,
  onStatusChange,
  onNotesChange,
  onHeightChange,
}) => {
  const [expanded, setExpanded] = useState(true);
  const panelRef = useRef<HTMLDivElement | null>(null);

  // Measure actual DOM height dynamically via ResizeObserver
  useEffect(() => {
    if (!panelRef.current) return;

    const updateHeight = () => {
      if (panelRef.current) {
        onHeightChange?.(panelRef.current.offsetHeight);
      }
    };

    updateHeight();

    const observer = new ResizeObserver(() => {
      updateHeight();
    });

    observer.observe(panelRef.current);
    return () => observer.disconnect();
  }, [task, expanded, onHeightChange]);

  // When no task is selected, show a permanent slim placeholder dock
  if (!task) {
    return (
      <Paper
        ref={panelRef}
        elevation={12}
        sx={{
          position: 'fixed',
          bottom: 0,
          left: 0,
          right: 0,
          zIndex: 1200,
          bgcolor: 'rgba(20, 18, 24, 0.95)',
          backdropFilter: 'blur(16px)',
          borderTop: '1px solid rgba(255, 255, 255, 0.08)',
          boxShadow: '0 -8px 30px rgba(0, 0, 0, 0.6)',
          py: 1.5,
          px: 3,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 1.5,
          userSelect: 'none',
        }}
      >
        <TouchAppIcon sx={{ fontSize: 20, color: 'primary.light', animation: 'bounce 2s infinite' }} />
        <Typography variant="body2" sx={{ color: 'text.secondary', fontSize: '0.84rem', fontWeight: 500 }}>
          タスクカードをクリックすると、ここに <strong style={{ color: '#d0bcff' }}>C# 記述例</strong>・<strong style={{ color: '#ffb74d' }}>課題</strong>・<strong style={{ color: '#81c784' }}>CUDA変換方針</strong>・<strong style={{ color: '#90caf9' }}>自動保存メモ</strong> の詳細が表示されます
        </Typography>
      </Paper>
    );
  }

  const currentStatus = task.status || 'incomplete';
  const details = task.details || {};
  const metrics = task.metrics || { blocker: 3, difficulty: 2, generality: 3, score: 30 };
  const tierInfo = getTierInfo(metrics.score);

  return (
    <Paper
      ref={panelRef}
      elevation={16}
      sx={{
        position: 'fixed',
        bottom: 0,
        left: 0,
        right: 0,
        zIndex: 1200,
        bgcolor: '#141218',
        backdropFilter: 'blur(20px)',
        borderTop: '1px solid rgba(255, 255, 255, 0.1)',
        boxShadow: '0 -12px 40px rgba(0, 0, 0, 0.7)',
        transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        maxHeight: '52vh',
        overflow: 'hidden',
        display: 'flex',
        flexDirection: 'column',
      }}
    >
      {/* Streamlined Header Bar */}
      <Box
        sx={{
          px: 3,
          py: 1.5,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          bgcolor: 'rgba(255, 255, 255, 0.02)',
          borderBottom: expanded ? '1px solid rgba(255, 255, 255, 0.06)' : 'none',
          cursor: 'pointer',
          userSelect: 'none',
        }}
        onClick={() => setExpanded((prev) => !prev)}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2, flexWrap: 'wrap', overflow: 'hidden' }}>
          <Chip
            label={task.id}
            size="small"
            sx={{ bgcolor: 'primary.dark', color: 'primary.light', fontWeight: 800, fontSize: '0.75rem', height: 22 }}
          />
          <Chip
            label={tierInfo.label}
            size="small"
            sx={{
              bgcolor: tierInfo.bg,
              color: tierInfo.color,
              border: `1px solid ${tierInfo.border}`,
              fontWeight: 700,
              fontSize: '0.72rem',
              height: 22,
            }}
          />
          <Chip
            label={task.domain}
            size="small"
            variant="outlined"
            title={DOMAIN_LABELS[task.domain] || task.domain}
            sx={{ fontSize: '0.7rem', color: 'text.secondary', borderColor: '#49454f', height: 22 }}
          />
          {task.category && (
            <Typography variant="caption" sx={{ color: 'text.secondary', fontSize: '0.75rem' }}>
              • {task.category}
            </Typography>
          )}
          <Typography
            variant="h6"
            noWrap
            sx={{
              fontWeight: 700,
              fontSize: '1.05rem',
              color: '#f4effa',
              ml: 0.5,
              maxWidth: { xs: 240, sm: 450, md: 650 },
            }}
          >
            {task.title}
          </Typography>
        </Box>

        {/* Right Status Toggle & Collapse Control */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Box sx={{ display: 'flex', gap: 0.5 }}>
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
                  fontSize: '0.72rem',
                  height: 24,
                  cursor: 'pointer',
                  opacity: currentStatus === s.val ? 1 : 0.5,
                  '&:hover': { opacity: 1, bgcolor: s.bg },
                }}
              />
            ))}
          </Box>
          <IconButton size="small" sx={{ color: 'text.secondary' }}>
            {expanded ? <CollapseIcon /> : <ExpandIcon />}
          </IconButton>
        </Box>
      </Box>

      {/* Expandable Content Area (Focused & De-cluttered Layout) */}
      <Collapse in={expanded} timeout="auto" unmountOnExit sx={{ overflowY: 'auto' }}>
        <Box sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 2.5 }}>
          {/* Sub-header: Single-line Minimal Metrics Summary */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, color: 'text.secondary', fontSize: '0.78rem' }}>
            <span>
              優先度スコア: <strong style={{ color: '#d0bcff', fontSize: '0.9rem' }}>{metrics.score.toFixed(1)}</strong>
            </span>
            <span style={{ opacity: 0.3 }}>|</span>
            <span>Blocker: <strong style={{ color: '#e6e0e9' }}>{metrics.blocker}</strong></span>
            <span>Difficulty: <strong style={{ color: '#e6e0e9' }}>{metrics.difficulty}</strong></span>
            <span>Generality: <strong style={{ color: '#e6e0e9' }}>{metrics.generality}</strong></span>
          </Box>

          {/* C# Code Example Section */}
          {details.csharp_example && (
            <Box>
              <Typography
                variant="caption"
                sx={{ color: '#90caf9', fontWeight: 700, mb: 0.6, display: 'flex', alignItems: 'center', gap: 0.6, fontSize: '0.75rem' }}
              >
                <CodeIcon sx={{ fontSize: 16 }} /> C# 記述例
              </Typography>
              <CsCodeHighlighter code={details.csharp_example} />
            </Box>
          )}

          {/* Side-by-Side Focused Cards (Minimal Accent Line Design) */}
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
            {details.current_behavior && (
              <Box
                sx={{
                  bgcolor: 'rgba(255, 183, 77, 0.04)',
                  p: 2,
                  borderRadius: '0 8px 8px 0',
                  borderLeft: '3px solid #ffb74d',
                }}
              >
                <Typography
                  variant="caption"
                  sx={{ color: '#ffb74d', fontWeight: 700, mb: 0.5, display: 'flex', alignItems: 'center', gap: 0.6, fontSize: '0.75rem' }}
                >
                  <WarningIcon sx={{ fontSize: 16 }} /> 現状の挙動 / 課題
                </Typography>
                <Typography variant="body2" sx={{ color: '#ffe0b2', lineHeight: 1.5, fontSize: '0.84rem' }}>
                  {details.current_behavior}
                </Typography>
              </Box>
            )}

            {(details.target_cuda || details.strategy) && (
              <Box
                sx={{
                  bgcolor: 'rgba(129, 199, 132, 0.04)',
                  p: 2,
                  borderRadius: '0 8px 8px 0',
                  borderLeft: '3px solid #81c784',
                }}
              >
                <Typography
                  variant="caption"
                  sx={{ color: '#81c784', fontWeight: 700, mb: 0.5, display: 'flex', alignItems: 'center', gap: 0.6, fontSize: '0.75rem' }}
                >
                  <CheckIcon sx={{ fontSize: 16 }} /> C++ (CUDA) 変換先 / 対策方針
                </Typography>
                <Typography variant="body2" sx={{ color: '#c8e6c9', lineHeight: 1.5, fontSize: '0.84rem' }}>
                  {details.target_cuda || details.strategy}
                </Typography>
              </Box>
            )}
          </Box>

          {/* Live Editable Notes (Clean Borderless Field) */}
          <Box>
            <Typography
              variant="caption"
              sx={{ color: '#d0bcff', fontWeight: 700, mb: 0.6, display: 'flex', alignItems: 'center', gap: 0.6, fontSize: '0.75rem' }}
            >
              <NoteIcon sx={{ fontSize: 16 }} /> タスク詳細メモ (自動保存)
            </Typography>
            <TextField
              multiline
              rows={2}
              fullWidth
              variant="outlined"
              placeholder="メモ・補足を自由に入力..."
              value={details.notes || ''}
              onChange={(e) => onNotesChange(task.id, e.target.value)}
              sx={{
                bgcolor: 'rgba(0, 0, 0, 0.25)',
                borderRadius: 2,
                '& .MuiOutlinedInput-root': {
                  fontSize: '0.84rem',
                  color: '#e6e0e9',
                  p: 1.5,
                  '& fieldset': { borderColor: 'rgba(255, 255, 255, 0.08)' },
                  '&:hover fieldset': { borderColor: 'rgba(208, 188, 255, 0.3)' },
                  '&.Mui-focused fieldset': { borderColor: 'primary.main' },
                },
              }}
            />
          </Box>
        </Box>
      </Collapse>
    </Paper>
  );
};
