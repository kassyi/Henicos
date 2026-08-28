import React from 'react';
import {
  Box, Card, TextField, InputAdornment, Typography, ToggleButtonGroup, ToggleButton,
  Tooltip, IconButton, CircularProgress, Button
} from '@mui/material';
import {
  Search as SearchIcon,
  Tune as TuneIcon,
  Undo as UndoIcon,
  Redo as RedoIcon,
  CloudDone as CloudDoneIcon,
  ErrorOutlined as ErrorIcon,
} from '@mui/icons-material';
import { JulesIcon } from './JulesIcon';

interface ControlsBarProps {
  searchQuery: string;
  setSearchQuery: (val: string) => void;
  completionFilter: string;
  setCompletionFilter: (val: any) => void;
  showAdvancedFilters: boolean;
  setShowAdvancedFilters: (val: boolean) => void;
  historyLengthPast: number;
  historyLengthFuture: number;
  handleUndo: () => void;
  handleRedo: () => void;
  saveStatus: 'saved' | 'saving' | 'error';
  cartIdsCount: number;
  copyJulesPrompt: () => void;
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
  searchQuery, setSearchQuery, completionFilter, setCompletionFilter,
  showAdvancedFilters, setShowAdvancedFilters, historyLengthPast, historyLengthFuture,
  handleUndo, handleRedo, saveStatus, cartIdsCount, copyJulesPrompt,
  setCartIds, setToast, minBlocker, setMinBlocker, minDifficulty, setMinDifficulty,
  minGenerality, setMinGenerality
}) => {
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
            sx={{ width: { xs: '100%', sm: 280 }, bgcolor: '#141218' }}
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
            <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.secondary', fontSize: '0.78rem', whiteSpace: 'nowrap' }}>
              表示:
            </Typography>
            <ToggleButtonGroup
              value={completionFilter}
              exclusive
              onChange={(_, val) => { if (val !== null) setCompletionFilter(val); }}
              sx={{ bgcolor: '#141218', border: '1px solid #49454f', borderRadius: 2 }}
            >
              <ToggleButton value="incomplete" sx={{ px: 1.2, height: 34, fontSize: '0.72rem', fontWeight: 700, color: 'text.secondary', borderColor: '#49454f', '&.Mui-selected': { bgcolor: '#4a4458', color: '#e8def8' } }}>未完了</ToggleButton>
              <ToggleButton value="implemented" sx={{ px: 1.2, height: 34, fontSize: '0.72rem', fontWeight: 700, color: 'text.secondary', borderColor: '#49454f', '&.Mui-selected': { bgcolor: '#2e5b44', color: '#c4eed0' } }}>🛠️ 実装済</ToggleButton>
              <ToggleButton value="tuned" sx={{ px: 1.2, height: 34, fontSize: '0.72rem', fontWeight: 700, color: 'text.secondary', borderColor: '#49454f', '&.Mui-selected': { bgcolor: '#5c256b', color: '#f3e5f5' } }}>⚡ パフォ済</ToggleButton>
              <ToggleButton value="completed" sx={{ px: 1.2, height: 34, fontSize: '0.72rem', fontWeight: 700, color: 'text.secondary', borderColor: '#49454f', '&.Mui-selected': { bgcolor: '#386a4f', color: '#c4eed0' } }}>完了</ToggleButton>
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
                  '&:hover': { bgcolor: 'rgba(208, 188, 255, 0.08)' }
                }}
              >
                <TuneIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Box>
        </Box>

        {/* Right: Actions, Undo/Redo & Auto-Save Indicator */}
        <Box sx={{ display: 'flex', gap: 1.2, alignItems: 'center', flexWrap: 'nowrap' }}>
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
              borderRadius: 2,
              bgcolor: cartIdsCount > 0 ? '#4a4458' : undefined,
              color: cartIdsCount > 0 ? '#e8def8' : undefined,
            }}
          >
            Jules依頼プロンプトを生成 ({cartIdsCount})
          </Button>

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
            to: { opacity: 1, transform: 'translateY(0)' }
          }
        }}
      >
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 3, alignItems: 'center' }}>
          <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.secondary', fontSize: '0.78rem' }}>
            因子フィルター (最小閾値):
          </Typography>

          {/* M3 Segmented Buttons: B */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2 }}>
            <Box sx={{ minWidth: 90 }}>
              <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.primary', fontSize: '0.78rem' }}>
                依存度 (B) &ge; <span style={{ color: '#d0bcff' }}>{minBlocker}</span>
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary', fontSize: '0.68rem', display: 'block', lineHeight: 1.1 }}>
                {minBlocker === 1 ? '小 (自己完結)' : minBlocker === 5 ? '大 (完全ブロック)' : `レベル ${minBlocker}`}
              </Typography>
            </Box>
            <ToggleButtonGroup
              value={minBlocker}
              exclusive
              onChange={(_, val) => { if (val !== null) setMinBlocker(val); }}
              sx={{ bgcolor: '#141218', border: '1px solid #49454f', borderRadius: 2 }}
            >
              {[
                { n: 1, color: '#66bb6a', label: '1 (最小)' },
                { n: 2, color: '#9ccc65', label: '2 (低)' },
                { n: 3, color: '#ffa726', label: '3 (中)' },
                { n: 4, color: '#ff7043', label: '4 (高)' },
                { n: 5, color: '#ef5350', label: '5 (最大)' },
              ].map(({ n, color, label }) => (
                <ToggleButton
                  key={n}
                  value={n}
                  title={label}
                  sx={{
                    minWidth: 32,
                    height: 30,
                    fontSize: '0.8rem',
                    fontWeight: 700,
                    color: 'text.secondary',
                    borderColor: '#49454f',
                    position: 'relative',
                    '&.Mui-selected': { bgcolor: color, color: '#141218', fontWeight: 800, '&:hover': { bgcolor: color } },
                    '&:hover': { bgcolor: 'rgba(255, 255, 255, 0.08)' },
                    '&::after': {
                      content: '""', position: 'absolute', bottom: 2, left: '20%', right: '20%',
                      height: 2, borderRadius: 1, bgcolor: color, opacity: minBlocker === n ? 0 : 0.6,
                    }
                  }}
                >
                  {n}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>

          {/* M3 Segmented Buttons: D */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2 }}>
            <Box sx={{ minWidth: 90 }}>
              <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.primary', fontSize: '0.78rem' }}>
                難易度 (D) &ge; <span style={{ color: '#d0bcff' }}>{minDifficulty}</span>
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary', fontSize: '0.68rem', display: 'block', lineHeight: 1.1 }}>
                {minDifficulty === 1 ? '極めて簡単' : minDifficulty === 5 ? '最難関/大設計' : `レベル ${minDifficulty}`}
              </Typography>
            </Box>
            <ToggleButtonGroup
              value={minDifficulty}
              exclusive
              onChange={(_, val) => { if (val !== null) setMinDifficulty(val); }}
              sx={{ bgcolor: '#141218', border: '1px solid #49454f', borderRadius: 2 }}
            >
              {[
                { n: 1, color: '#66bb6a', label: '1 (半日/極めて簡単)' },
                { n: 2, color: '#9ccc65', label: '2 (やや容易)' },
                { n: 3, color: '#ffa726', label: '3 (中程度)' },
                { n: 4, color: '#ff7043', label: '4 (難関)' },
                { n: 5, color: '#ef5350', label: '5 (最難関/大設計)' },
              ].map(({ n, color, label }) => (
                <ToggleButton
                  key={n}
                  value={n}
                  title={label}
                  sx={{
                    minWidth: 32, height: 30, fontSize: '0.8rem', fontWeight: 700,
                    color: 'text.secondary', borderColor: '#49454f', position: 'relative',
                    '&.Mui-selected': { bgcolor: color, color: '#141218', fontWeight: 800, '&:hover': { bgcolor: color } },
                    '&:hover': { bgcolor: 'rgba(255, 255, 255, 0.08)' },
                    '&::after': {
                      content: '""', position: 'absolute', bottom: 2, left: '20%', right: '20%',
                      height: 2, borderRadius: 1, bgcolor: color, opacity: minDifficulty === n ? 0 : 0.6,
                    }
                  }}
                >
                  {n}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>

          {/* M3 Segmented Buttons: G */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2 }}>
            <Box sx={{ minWidth: 90 }}>
              <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.primary', fontSize: '0.78rem' }}>
                汎用性 (G) &ge; <span style={{ color: '#d0bcff' }}>{minGenerality}</span>
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary', fontSize: '0.68rem', display: 'block', lineHeight: 1.1 }}>
                {minGenerality === 1 ? 'ニッチ' : minGenerality === 5 ? '常時使われる' : `レベル ${minGenerality}`}
              </Typography>
            </Box>
            <ToggleButtonGroup
              value={minGenerality}
              exclusive
              onChange={(_, val) => { if (val !== null) setMinGenerality(val); }}
              sx={{ bgcolor: '#141218', border: '1px solid #49454f', borderRadius: 2 }}
            >
              {[
                { n: 1, color: '#90caf9', label: '1 (ニッチ/稀)' },
                { n: 2, color: '#42a5f5', label: '2 (限定的)' },
                { n: 3, color: '#1e88e5', label: '3 (通常)' },
                { n: 4, color: '#1565c0', label: '4 (頻繁に利用)' },
                { n: 5, color: '#0d47a1', label: '5 (常時使われる/基盤)' },
              ].map(({ n, color, label }) => (
                <ToggleButton
                  key={n}
                  value={n}
                  title={label}
                  sx={{
                    minWidth: 32, height: 30, fontSize: '0.8rem', fontWeight: 700,
                    color: 'text.secondary', borderColor: '#49454f', position: 'relative',
                    '&.Mui-selected': { bgcolor: color, color: '#ffffff', fontWeight: 800, '&:hover': { bgcolor: color } },
                    '&:hover': { bgcolor: 'rgba(255, 255, 255, 0.08)' },
                    '&::after': {
                      content: '""', position: 'absolute', bottom: 2, left: '20%', right: '20%',
                      height: 2, borderRadius: 1, bgcolor: color, opacity: minGenerality === n ? 0 : 0.6,
                    }
                  }}
                >
                  {n}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>
        </Box>
      </Box>
    </Card>
  );
};
