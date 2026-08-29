import React, { useState, useEffect, useRef } from 'react';
import {
  Paper,
  Box,
  Typography,
  Chip,
  IconButton,
  TextField,
  Collapse,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
} from '@mui/material';
import {
  KeyboardArrowDown as CollapseIcon,
  KeyboardArrowUp as ExpandIcon,
  Code as CodeIcon,
  ReportProblemOutlined as WarningIcon,
  CheckCircleOutlined as CheckIcon,
  EditNote as NoteIcon,
  TouchAppOutlined as TouchAppIcon,
  VerticalAlignBottom as DockBottomIcon,
  BorderLeft as DockLeftIcon,
  BorderRight as DockRightIcon,
  Close as CloseIcon,
} from '@mui/icons-material';
import { DOMAIN_LABELS } from '../types';
import type { RoadmapTask, TaskStatus, DockPosition } from '../types';
import { CsCodeHighlighter } from './CsCodeHighlighter';

interface TaskDetailPanelProps {
  task: RoadmapTask | null;
  dockPosition: DockPosition;
  onDockPositionChange: (pos: DockPosition) => void;
  onStatusChange: (id: string, newStatus: TaskStatus) => void;
  onNotesChange: (id: string, notes: string) => void;
  onClose?: () => void;
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
  dockPosition,
  onDockPositionChange,
  onStatusChange,
  onNotesChange,
  onClose,
  onHeightChange,
}) => {
  const [expanded, setExpanded] = useState(true);
  const panelRef = useRef<HTMLDivElement | null>(null);

  const isVertical = dockPosition === 'left' || dockPosition === 'right';

  // Measure actual DOM height dynamically via ResizeObserver (mainly for bottom dock)
  useEffect(() => {
    if (!panelRef.current) return;

    const updateHeight = () => {
      if (panelRef.current) {
        onHeightChange?.(dockPosition === 'bottom' ? panelRef.current.offsetHeight : 0);
      }
    };

    updateHeight();

    const observer = new ResizeObserver(() => {
      updateHeight();
    });

    observer.observe(panelRef.current);
    return () => observer.disconnect();
  }, [task, expanded, dockPosition, onHeightChange]);

  // Dock Switcher Control Button Group
  const renderDockControls = () => (
    <ToggleButtonGroup
      size="small"
      value={dockPosition}
      exclusive
      onChange={(_, newPos) => {
        if (newPos) onDockPositionChange(newPos);
      }}
      sx={{
        height: 24,
        bgcolor: 'rgba(255, 255, 255, 0.04)',
        borderRadius: 1.5,
        '& .MuiToggleButton-root': {
          px: 0.8,
          py: 0,
          border: 'none',
          color: 'text.secondary',
          '&.Mui-selected': {
            bgcolor: 'primary.dark',
            color: 'primary.light',
          },
          '&:hover': {
            bgcolor: 'rgba(255, 255, 255, 0.08)',
          },
        },
      }}
    >
      <Tooltip title="左側に固定" arrow>
        <ToggleButton value="left" aria-label="左側に固定">
          <DockLeftIcon sx={{ fontSize: 15 }} />
        </ToggleButton>
      </Tooltip>
      <Tooltip title="下部に固定" arrow>
        <ToggleButton value="bottom" aria-label="下部に固定">
          <DockBottomIcon sx={{ fontSize: 15 }} />
        </ToggleButton>
      </Tooltip>
      <Tooltip title="右側に固定" arrow>
        <ToggleButton value="right" aria-label="右側に固定">
          <DockRightIcon sx={{ fontSize: 15 }} />
        </ToggleButton>
      </Tooltip>
    </ToggleButtonGroup>
  );

  // When no task is selected, show a permanent placeholder dock with dock switcher
  if (!task) {
    if (isVertical) {
      return (
        <Paper
          ref={panelRef}
          elevation={12}
          sx={{
            position: 'fixed',
            top: 0,
            bottom: 0,
            left: dockPosition === 'left' ? 0 : 'auto',
            right: dockPosition === 'right' ? 0 : 'auto',
            width: { xs: '100vw', sm: 420, md: 460 },
            zIndex: 1200,
            bgcolor: 'rgba(20, 18, 24, 0.95)',
            backdropFilter: 'blur(16px)',
            borderLeft: dockPosition === 'right' ? '1px solid rgba(255, 255, 255, 0.08)' : 'none',
            borderRight: dockPosition === 'left' ? '1px solid rgba(255, 255, 255, 0.08)' : 'none',
            boxShadow:
              dockPosition === 'left'
                ? '8px 0 30px rgba(0, 0, 0, 0.6)'
                : '-8px 0 30px rgba(0, 0, 0, 0.6)',
            p: 2.5,
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            userSelect: 'none',
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', pb: 2, borderBottom: '1px solid rgba(255, 255, 255, 0.06)' }}>
            <Typography variant="subtitle2" sx={{ color: 'text.secondary', fontWeight: 700, fontSize: '0.82rem' }}>
              タスク詳細パネル
            </Typography>
            {renderDockControls()}
          </Box>

          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 1.5, my: 'auto', textAlign: 'center', px: 2 }}>
            <TouchAppIcon sx={{ fontSize: 32, color: 'primary.light', opacity: 0.8 }} />
            <Typography variant="body2" sx={{ color: 'text.secondary', fontSize: '0.84rem', lineHeight: 1.6 }}>
              タスクカードをクリックすると<br />
              ここに詳細（<strong style={{ color: '#d0bcff' }}>C#記述例</strong>、<strong style={{ color: '#ffb74d' }}>課題</strong>、<strong style={{ color: '#81c784' }}>CUDA方針</strong>、<strong style={{ color: '#90caf9' }}>自動保存メモ</strong>）が表示されます。
            </Typography>
          </Box>

          <Box sx={{ textAlign: 'center', pt: 2, borderTop: '1px solid rgba(255, 255, 255, 0.06)' }}>
            <Typography variant="caption" sx={{ color: 'text.disabled', fontSize: '0.72rem' }}>
              ドッキング位置は上部アイコンで変更可能
            </Typography>
          </Box>
        </Paper>
      );
    }

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
          py: 1.2,
          px: 3,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 2,
          userSelect: 'none',
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <TouchAppIcon sx={{ fontSize: 18, color: 'primary.light' }} />
          <Typography variant="body2" sx={{ color: 'text.secondary', fontSize: '0.82rem', fontWeight: 500 }}>
            タスクカードをクリックすると、ここに <strong style={{ color: '#d0bcff' }}>C# 記述例</strong>・<strong style={{ color: '#ffb74d' }}>課題</strong>・<strong style={{ color: '#81c784' }}>CUDA変換方針</strong>・<strong style={{ color: '#90caf9' }}>自動保存メモ</strong> の詳細が表示されます
          </Typography>
        </Box>
        {renderDockControls()}
      </Paper>
    );
  }

  const currentStatus = task.status || 'incomplete';
  const details = task.details || {};
  const metrics = task.metrics || { blocker: 3, difficulty: 2, generality: 3, score: 30 };
  const tierInfo = getTierInfo(metrics.score);

  // Container styling depending on dock position
  const paperStyles = isVertical
    ? {
        position: 'fixed' as const,
        top: 0,
        bottom: 0,
        left: dockPosition === 'left' ? 0 : 'auto',
        right: dockPosition === 'right' ? 0 : 'auto',
        width: { xs: '100vw', sm: 420, md: 460 },
        height: '100vh',
        zIndex: 1200,
        bgcolor: '#141218',
        backdropFilter: 'blur(20px)',
        borderLeft: dockPosition === 'right' ? '1px solid rgba(255, 255, 255, 0.1)' : 'none',
        borderRight: dockPosition === 'left' ? '1px solid rgba(255, 255, 255, 0.1)' : 'none',
        boxShadow:
          dockPosition === 'left'
            ? '12px 0 40px rgba(0, 0, 0, 0.75)'
            : '-12px 0 40px rgba(0, 0, 0, 0.75)',
        transition: 'width 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        display: 'flex',
        flexDirection: 'column' as const,
      }
    : {
        position: 'fixed' as const,
        bottom: 0,
        left: 0,
        right: 0,
        zIndex: 1200,
        bgcolor: '#141218',
        backdropFilter: 'blur(20px)',
        borderTop: '1px solid rgba(255, 255, 255, 0.1)',
        boxShadow: '0 -12px 40px rgba(0, 0, 0, 0.7)',
        transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        maxHeight: '55vh',
        overflow: 'hidden',
        display: 'flex',
        flexDirection: 'column' as const,
      };

  return (
    <Paper ref={panelRef} elevation={16} sx={paperStyles}>
      {/* Streamlined Header Bar */}
      <Box
        sx={{
          px: 2.5,
          py: 1.5,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          bgcolor: 'rgba(255, 255, 255, 0.02)',
          borderBottom: '1px solid rgba(255, 255, 255, 0.06)',
          userSelect: 'none',
          flexShrink: 0,
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', overflow: 'hidden' }}>
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
          <Typography
            variant="subtitle1"
            noWrap
            sx={{
              fontWeight: 700,
              fontSize: '0.95rem',
              color: '#f4effa',
              ml: 0.5,
              maxWidth: isVertical ? { xs: 180, sm: 220 } : { xs: 220, sm: 400, md: 550 },
            }}
            title={task.title}
          >
            {task.title}
          </Typography>
        </Box>

        {/* Right Controls: Dock position switcher, status selector, and collapse/close button */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexShrink: 0 }}>
          {renderDockControls()}

          {!isVertical && (
            <IconButton
              size="small"
              onClick={() => setExpanded((prev) => !prev)}
              sx={{ color: 'text.secondary', p: 0.5 }}
            >
              {expanded ? <CollapseIcon sx={{ fontSize: 20 }} /> : <ExpandIcon sx={{ fontSize: 20 }} />}
            </IconButton>
          )}

          {onClose && (
            <IconButton size="small" onClick={onClose} sx={{ color: 'text.secondary', p: 0.5 }}>
              <CloseIcon sx={{ fontSize: 18 }} />
            </IconButton>
          )}
        </Box>
      </Box>

      {/* Main Content Area */}
      {isVertical ? (
        <Box
          sx={{
            p: 2.5,
            display: 'flex',
            flexDirection: 'column',
            gap: 2.5,
            overflowY: 'auto',
            flex: 1,
          }}
        >
          {/* Status Chip Options (Full Width in Vertical Dock) */}
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', bgcolor: 'rgba(255, 255, 255, 0.02)', p: 1, borderRadius: 2 }}>
            <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, fontSize: '0.75rem', pl: 0.5 }}>
              進捗ステータス:
            </Typography>
            <Box sx={{ display: 'flex', gap: 0.6 }}>
              {STATUS_CHIP_OPTIONS.map((s) => (
                <Chip
                  key={s.val}
                  label={s.label}
                  size="small"
                  onClick={() => onStatusChange(task.id, s.val)}
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

          {/* Metrics summary */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, color: 'text.secondary', fontSize: '0.75rem', flexWrap: 'wrap' }}>
            <span>
              優先度スコア: <strong style={{ color: '#d0bcff', fontSize: '0.85rem' }}>{metrics.score.toFixed(1)}</strong>
            </span>
            <span style={{ opacity: 0.3 }}>|</span>
            <span>Blocker: <strong style={{ color: '#e6e0e9' }}>{metrics.blocker}</strong></span>
            <span>Difficulty: <strong style={{ color: '#e6e0e9' }}>{metrics.difficulty}</strong></span>
            <span>Generality: <strong style={{ color: '#e6e0e9' }}>{metrics.generality}</strong></span>
          </Box>

          {/* C# Code Example */}
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

          {/* Current Behavior / Issues */}
          {details.current_behavior && (
            <Box
              sx={{
                bgcolor: 'rgba(255, 183, 77, 0.04)',
                p: 1.8,
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
              <Typography variant="body2" sx={{ color: '#ffe0b2', lineHeight: 1.5, fontSize: '0.82rem' }}>
                {details.current_behavior}
              </Typography>
            </Box>
          )}

          {/* Target CUDA / Strategy */}
          {(details.target_cuda || details.strategy) && (
            <Box
              sx={{
                bgcolor: 'rgba(129, 199, 132, 0.04)',
                p: 1.8,
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
              <Typography variant="body2" sx={{ color: '#c8e6c9', lineHeight: 1.5, fontSize: '0.82rem' }}>
                {details.target_cuda || details.strategy}
              </Typography>
            </Box>
          )}

          {/* Notes */}
          <Box>
            <Typography
              variant="caption"
              sx={{ color: '#d0bcff', fontWeight: 700, mb: 0.6, display: 'flex', alignItems: 'center', gap: 0.6, fontSize: '0.75rem' }}
            >
              <NoteIcon sx={{ fontSize: 16 }} /> タスク詳細メモ (自動保存)
            </Typography>
            <TextField
              multiline
              rows={3}
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
      ) : (
        <Collapse in={expanded} timeout="auto" unmountOnExit sx={{ overflowY: 'auto' }}>
          <Box sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 2.5 }}>
            {/* Status Options & Metrics in Bottom Dock */}
            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1.5 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, color: 'text.secondary', fontSize: '0.78rem' }}>
                <span>
                  優先度スコア: <strong style={{ color: '#d0bcff', fontSize: '0.9rem' }}>{metrics.score.toFixed(1)}</strong>
                </span>
                <span style={{ opacity: 0.3 }}>|</span>
                <span>Blocker: <strong style={{ color: '#e6e0e9' }}>{metrics.blocker}</strong></span>
                <span>Difficulty: <strong style={{ color: '#e6e0e9' }}>{metrics.difficulty}</strong></span>
                <span>Generality: <strong style={{ color: '#e6e0e9' }}>{metrics.generality}</strong></span>
              </Box>

              <Box sx={{ display: 'flex', gap: 0.6 }}>
                {STATUS_CHIP_OPTIONS.map((s) => (
                  <Chip
                    key={s.val}
                    label={s.label}
                    size="small"
                    onClick={() => onStatusChange(task.id, s.val)}
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

            {/* Side-by-Side Focused Cards */}
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

            {/* Live Editable Notes */}
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
      )}
    </Paper>
  );
};
