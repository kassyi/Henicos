import React from 'react';
import {
  Box,
  Card,
  TextField,
  InputAdornment,
  Typography,
  ToggleButtonGroup,
  ToggleButton,
  Tooltip,
  IconButton,
  CircularProgress,
  Button,
} from '@mui/material';
import {
  Search as SearchIcon,
  Tune as TuneIcon,
  Undo as UndoIcon,
  Redo as RedoIcon,
  CloudDone as CloudDoneIcon,
  ErrorOutlined as ErrorIcon,
  ContentCopy as CopyIcon,
} from '@mui/icons-material';
import { JulesIcon } from './JulesIcon';
import { FactorFilterControl, type FactorOption } from './FactorFilterControl';
import type { TaskStatus } from '../types';

const BLOCKER_OPTIONS: FactorOption[] = [
  { n: 1, color: '#66bb6a', label: '1 (最小)' },
  { n: 2, color: '#9ccc65', label: '2 (低)' },
  { n: 3, color: '#ffa726', label: '3 (中)' },
  { n: 4, color: '#ff7043', label: '4 (高)' },
  { n: 5, color: '#ef5350', label: '5 (最大)' },
];

const DIFFICULTY_OPTIONS: FactorOption[] = [
  { n: 1, color: '#66bb6a', label: '1 (半日/極めて簡単)' },
  { n: 2, color: '#9ccc65', label: '2 (やや容易)' },
  { n: 3, color: '#ffa726', label: '3 (中程度)' },
  { n: 4, color: '#ff7043', label: '4 (難関)' },
  { n: 5, color: '#ef5350', label: '5 (最難関/大設計)' },
];

const GENERALITY_OPTIONS: FactorOption[] = [
  { n: 1, color: '#90caf9', label: '1 (ニッチ/稀)' },
  { n: 2, color: '#42a5f5', label: '2 (限定的)' },
  { n: 3, color: '#1e88e5', label: '3 (通常)' },
  { n: 4, color: '#1565c0', label: '4 (頻繁に利用)' },
  { n: 5, color: '#0d47a1', label: '5 (常時使われる/基盤)' },
];

interface ControlsBarProps {
  searchQuery: string;
  setSearchQuery: (val: string) => void;
  statusFilters: TaskStatus[];
  setStatusFilters: (val: TaskStatus[]) => void;
  showAdvancedFilters: boolean;
  setShowAdvancedFilters: (val: boolean) => void;
  historyLengthPast: number;
  historyLengthFuture: number;
  handleUndo: () => void;
  handleRedo: () => void;
  saveStatus: 'saved' | 'saving' | 'error';
  cartIdsCount: number;
  copyJulesPrompt: () => void;
  copyCartIdsOnly: () => void;
  setCartIds: (ids: string[]) => void;
  setToast: (toast: any) => void;
  minBlocker: number;
  setMinBlocker: (val: number) => void;
  minDifficulty: number;
  setMinDifficulty: (val: number) => void;
  minGenerality: number;
  setMinGenerality: (val: number) => void;
}

export const ControlsBar: React.FC<ControlsBarProps> = ({
  searchQuery,
  setSearchQuery,
  statusFilters,
  setStatusFilters,
  showAdvancedFilters,
  setShowAdvancedFilters,
  historyLengthPast,
  historyLengthFuture,
  handleUndo,
  handleRedo,
  saveStatus,
  cartIdsCount,
  copyJulesPrompt,
  copyCartIdsOnly,
  setCartIds,
  setToast,
  minBlocker,
  setMinBlocker,
  minDifficulty,
  setMinDifficulty,
  minGenerality,
  setMinGenerality,
}) => {
  const blockerSubLabel =
    minBlocker === 1 ? '小 (自己完結)' : minBlocker === 5 ? '大 (完全ブロック)' : `レベル ${minBlocker}`;
  const difficultySubLabel =
    minDifficulty === 1 ? '極めて簡単' : minDifficulty === 5 ? '最難関/大設計' : `レベル ${minDifficulty}`;
  const generalitySubLabel =
    minGenerality === 1 ? 'ニッチ' : minGenerality === 5 ? '常時使われる' : `レベル ${minGenerality}`;

  return (
    <Card
      sx={{
        p: 2.5,
        mb: 2.5,
        bgcolor: '#211f26',
        borderRadius: 2,
        border: '1px solid #49454f',
        '&:hover': {
          transform: 'none',
          borderColor: '#49454f',
          bgcolor: '#211f26',
        },
      }}
    >
      {/* Row 1: Primary Search, Status & Actions */}
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 2,
          alignItems: 'center',
          justifyContent: 'space-between',
          pb: showAdvancedFilters ? 2 : 0,
          borderBottom: showAdvancedFilters ? '1px solid rgba(255, 255, 255, 0.08)' : 'none',
        }}
      >
        {/* Left: Search & Status */}
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'center', flex: 1, minWidth: 320 }}>
          <TextField
            size="small"
            placeholder="タスク名、ID、メモを検索..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            sx={{
              width: { xs: '100%', sm: 280 },
              '& .MuiOutlinedInput-root': {
                bgcolor: '#141218',
                borderRadius: 2,
              },
            }}
            slotProps={{
              input: {
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon sx={{ color: 'text.secondary' }} />
                  </InputAdornment>
                ),
              },
            }}
          />

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Typography
              variant="body2"
              sx={{ fontWeight: 700, color: 'text.secondary', fontSize: '0.78rem', whiteSpace: 'nowrap' }}
            >
              表示:
            </Typography>

            {/* Incomplete Toggle */}
            <ToggleButtonGroup
              value={statusFilters.includes('incomplete') ? 'incomplete' : null}
              exclusive
              onChange={(_, val) => {
                if (val === 'incomplete') {
                  setStatusFilters(['incomplete']);
                }
              }}
              sx={{
                bgcolor: '#141218',
                border: '1px solid #49454f',
                borderRadius: 2,
                overflow: 'hidden',
                '& .MuiToggleButtonGroup-grouped': {
                  border: 0,
                  borderRadius: 0,
                },
              }}
            >
              <ToggleButton
                value="incomplete"
                sx={{
                  px: 1.4,
                  height: 34,
                  fontSize: '0.72rem',
                  fontWeight: 700,
                  color: 'text.secondary',
                  '&.Mui-selected': { bgcolor: '#4a4458', color: '#e8def8', '&:hover': { bgcolor: '#3e3849' } },
                }}
              >
                未完了
              </ToggleButton>
            </ToggleButtonGroup>

            {/* Completed / Tuned Multi-select Toggle */}
            <ToggleButtonGroup
              value={statusFilters.filter((s) => s !== 'incomplete')}
              onChange={(_, newValues: TaskStatus[]) => {
                if (newValues.length === 0) {
                  setStatusFilters(['incomplete']);
                } else {
                  setStatusFilters(newValues);
                }
              }}
              sx={{
                bgcolor: '#141218',
                border: '1px solid #49454f',
                borderRadius: 2,
                overflow: 'hidden',
                '& .MuiToggleButtonGroup-grouped': {
                  border: 0,
                  borderRadius: 0,
                  '&:not(:first-of-type)': {
                    borderLeft: '1px solid #49454f',
                  },
                },
              }}
            >
              <ToggleButton
                value="implemented"
                sx={{
                  px: 1.4,
                  height: 34,
                  fontSize: '0.72rem',
                  fontWeight: 700,
                  color: 'text.secondary',
                  '&.Mui-selected': { bgcolor: '#2e5b44', color: '#c4eed0', '&:hover': { bgcolor: '#244836' } },
                }}
              >
                🛠️ 実装済
              </ToggleButton>
              <ToggleButton
                value="tuned"
                sx={{
                  px: 1.4,
                  height: 34,
                  fontSize: '0.72rem',
                  fontWeight: 700,
                  color: 'text.secondary',
                  '&.Mui-selected': { bgcolor: '#5c256b', color: '#f3e5f5', '&:hover': { bgcolor: '#4a1e56' } },
                }}
              >
                ⚡ パフォ済
              </ToggleButton>
            </ToggleButtonGroup>

            <Tooltip title="詳細フィルター (B/D/G)">
              <IconButton
                size="small"
                onClick={() => setShowAdvancedFilters(!showAdvancedFilters)}
                sx={{
                  ml: 1,
                  bgcolor: showAdvancedFilters ? 'rgba(208, 188, 255, 0.12)' : 'transparent',
                  color: showAdvancedFilters ? 'primary.light' : 'text.secondary',
                  border: '1px solid',
                  borderColor: showAdvancedFilters ? 'primary.main' : 'transparent',
                  '&:hover': { bgcolor: 'rgba(208, 188, 255, 0.08)' },
                }}
              >
                <TuneIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Box>
        </Box>

        {/* Right: Actions, Undo/Redo & Auto-Save Indicator */}
        <Box sx={{ display: 'flex', gap: 1.2, alignItems: 'center', flexWrap: 'wrap', justifyContent: 'flex-start' }}>
          {/* Undo / Redo Buttons */}
          <Box sx={{ display: 'flex', bgcolor: '#141218', border: '1px solid #49454f', borderRadius: 2, p: 0.3 }}>
            <Tooltip title="元に戻す (Ctrl+Z)">
              <span>
                <IconButton
                  size="small"
                  onClick={handleUndo}
                  disabled={historyLengthPast === 0}
                  sx={{ color: historyLengthPast > 0 ? '#e8def8' : 'text.disabled', p: 0.6 }}
                >
                  <UndoIcon fontSize="small" />
                </IconButton>
              </span>
            </Tooltip>
            <Tooltip title="やり直す (Ctrl+Y / Ctrl+Shift+Z)">
              <span>
                <IconButton
                  size="small"
                  onClick={handleRedo}
                  disabled={historyLengthFuture === 0}
                  sx={{ color: historyLengthFuture > 0 ? '#e8def8' : 'text.disabled', p: 0.6 }}
                >
                  <RedoIcon fontSize="small" />
                </IconButton>
              </span>
            </Tooltip>
          </Box>

          {/* Auto-Save Indicator */}
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 0.8,
              px: 1.5,
              py: 0.7,
              bgcolor: '#141218',
              border: '1px solid #49454f',
              borderRadius: 2,
            }}
          >
            {saveStatus === 'saving' && (
              <>
                <CircularProgress size={13} color="primary" />
                <Typography variant="caption" sx={{ color: 'primary.light', fontWeight: 600, fontSize: '0.72rem' }}>
                  自動保存中...
                </Typography>
              </>
            )}
            {saveStatus === 'saved' && (
              <>
                <CloudDoneIcon sx={{ fontSize: 16, color: '#66bb6a' }} />
                <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, fontSize: '0.72rem' }}>
                  自動保存済
                </Typography>
              </>
            )}
            {saveStatus === 'error' && (
              <>
                <ErrorIcon sx={{ fontSize: 16, color: '#ef5350' }} />
                <Typography variant="caption" sx={{ color: '#ef5350', fontWeight: 600, fontSize: '0.72rem' }}>
                  保存失敗
                </Typography>
              </>
            )}
          </Box>

          {/* Jules & ID Copy Button Group */}
          <Box sx={{ display: 'flex', borderRadius: 2, overflow: 'hidden', alignItems: 'stretch' }}>
            <Button
              variant="contained"
              color="secondary"
              startIcon={<JulesIcon />}
              onClick={copyJulesPrompt}
              disabled={cartIdsCount === 0}
              sx={{
                px: 2.2,
                py: 0.9,
                fontWeight: 700,
                fontSize: '0.82rem',
                borderTopRightRadius: 0,
                borderBottomRightRadius: 0,
                bgcolor: cartIdsCount > 0 ? '#4a4458' : undefined,
                color: cartIdsCount > 0 ? '#e8def8' : undefined,
              }}
            >
              Jules依頼プロンプトを生成 ({cartIdsCount})
            </Button>
            <Tooltip title="タスクIDのみをコピー">
              <span style={{ display: 'flex' }}>
                <Button
                  variant="contained"
                  color="secondary"
                  onClick={copyCartIdsOnly}
                  disabled={cartIdsCount === 0}
                  sx={{
                    minWidth: 40,
                    px: 1,
                    py: 0.9,
                    height: '100%',
                    borderTopLeftRadius: 0,
                    borderBottomLeftRadius: 0,
                    borderLeft: '1px solid rgba(0,0,0,0.2)',
                    bgcolor: cartIdsCount > 0 ? '#4a4458' : undefined,
                    color: cartIdsCount > 0 ? '#e8def8' : undefined,
                  }}
                >
                  <CopyIcon fontSize="small" />
                </Button>
              </span>
            </Tooltip>
          </Box>

          {cartIdsCount > 0 && (
            <Button
              variant="text"
              color="inherit"
              size="small"
              onClick={() => {
                setCartIds([]);
                setToast({ open: true, message: 'カートを空にしました' });
              }}
              sx={{ color: 'text.secondary', fontSize: '0.8rem', minWidth: 'auto', px: 1 }}
            >
              クリア
            </Button>
          )}
        </Box>
      </Box>

      {/* Row 2: Factor Filters (B / D / G) */}
      <Box
        sx={{
          display: showAdvancedFilters ? 'flex' : 'none',
          flexWrap: 'wrap',
          gap: 3,
          alignItems: 'center',
          justifyContent: 'space-between',
          pt: 2,
          animation: 'fadeIn 0.2s ease-in-out',
          '@keyframes fadeIn': {
            from: { opacity: 0, transform: 'translateY(-10px)' },
            to: { opacity: 1, transform: 'translateY(0)' },
          },
        }}
      >
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 3, alignItems: 'center' }}>
          <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.secondary', fontSize: '0.78rem' }}>
            因子フィルター (最小閾値):
          </Typography>

          <FactorFilterControl
            name="依存度"
            symbol="B"
            value={minBlocker}
            subLabel={blockerSubLabel}
            options={BLOCKER_OPTIONS}
            onChange={setMinBlocker}
          />

          <FactorFilterControl
            name="難易度"
            symbol="D"
            value={minDifficulty}
            subLabel={difficultySubLabel}
            options={DIFFICULTY_OPTIONS}
            onChange={setMinDifficulty}
          />

          <FactorFilterControl
            name="汎用性"
            symbol="G"
            value={minGenerality}
            subLabel={generalitySubLabel}
            options={GENERALITY_OPTIONS}
            onChange={setMinGenerality}
          />
        </Box>
      </Box>
    </Card>
  );
};
