import React, { useState, useEffect } from 'react';
import {
  ThemeProvider,
  CssBaseline,
  Container,
  Box,
  Typography,
  Card,
  Snackbar,
  Alert,
  Button,
  CircularProgress,
} from '@mui/material';
import { m3DarkTheme } from './theme';
import { useRoadmap } from './hooks/useRoadmap';
import { ControlsBar } from './components/ControlsBar';
import { InfoAccordion } from './components/InfoAccordion';
import { TierSection } from './components/TierSection';
import { TaskDetailPanel } from './components/TaskDetailPanel';
import type { DockPosition } from './types';

const TIER_DEFINITIONS = [
  { tier: 1, title: 'Tier 1: 最優先基盤 (スコア 36以上)' },
  { tier: 2, title: 'Tier 2: 優先構文 (スコア 28〜35)' },
  { tier: 3, title: 'Tier 3: 通常構文 (スコア 20〜27)' },
  { tier: 4, title: 'Tier 4: 後回し / 独自拡張 (スコア 19以下)' },
];

export const App: React.FC = () => {
  const {
    tasks,
    loading,
    saveStatus,
    selectedTaskId,
    setSelectedTaskId,
    history,
    handleUndo,
    handleRedo,
    searchQuery,
    setSearchQuery,
    progressFilter,
    setProgressFilter,
    showAdvancedFilters,
    setShowAdvancedFilters,
    minBlocker,
    setMinBlocker,
    minDifficulty,
    setMinDifficulty,
    minGenerality,
    setMinGenerality,
    toast,
    setToast,
    cartIds,
    setCartIds,
    handleCartClick,
    updateTaskStatus,
    updateNotes,
    toggleTierCart,
    copyJulesPrompt,
    copyCartIdsOnly,
    filteredTasks,
    tasksByTier,
    exitingIds,
    statusCounts,
    deferredFilterKey,
  } = useRoadmap();

  // Dock position preference (bottom, left, right)
  const [dockPosition, setDockPosition] = useState<DockPosition>(() => {
    const saved = localStorage.getItem('roadmap_dock_position');
    if (saved === 'left' || saved === 'right' || saved === 'bottom') return saved;
    return 'bottom';
  });

  useEffect(() => {
    localStorage.setItem('roadmap_dock_position', dockPosition);
  }, [dockPosition]);

  // Dynamically measured physical height of the bottom TaskDetailPanel
  const [panelHeight, setPanelHeight] = useState<number>(64);

  // Find currently selected task object
  const selectedTask = selectedTaskId ? tasks.find((t) => t.id === selectedTaskId) || null : null;

  if (loading) {
    return (
      <ThemeProvider theme={m3DarkTheme}>
        <CssBaseline />
        <Box
          sx={{
            display: 'flex',
            minHeight: '100vh',
            alignItems: 'center',
            justifyContent: 'center',
            bgcolor: '#141218',
          }}
        >
          <CircularProgress color="primary" />
        </Box>
      </ThemeProvider>
    );
  }

  // Dynamic layout margins based on dock position
  const mainWrapperStyles =
    dockPosition === 'bottom'
      ? {
          width: '100%',
          pb: `${panelHeight + 32}px`,
          transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        }
      : dockPosition === 'left'
      ? {
          width: { xs: '100%', md: 'calc(100% - 600px)' },
          ml: { xs: 0, md: '600px' },
          pb: 6,
          transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        }
      : {
          width: { xs: '100%', md: 'calc(100% - 600px)' },
          mr: { xs: 0, md: '600px' },
          pb: 6,
          transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        };

  return (
    <ThemeProvider theme={m3DarkTheme}>
      <CssBaseline />
      <Box sx={{ minHeight: '100vh', bgcolor: '#141218' }}>
        <Box sx={mainWrapperStyles}>
          <Container
            maxWidth="xl"
            sx={{
              pt: 4,
              px: { xs: 2, sm: 3 },
            }}
          >
        {/* Top App Header */}
        <Box sx={{ textAlign: 'center', mb: 3 }}>
          <Typography variant="h4" component="h1" sx={{ color: 'primary.main', mb: 1, fontWeight: 800 }}>
            Henicos Roadmap Manager
          </Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            インタラクティブ進捗管理システム — 全 <strong>{tasks.length}</strong> 件中 <strong>{filteredTasks.length}</strong> 件表示
          </Typography>
        </Box>

        <ControlsBar
          searchQuery={searchQuery}
          setSearchQuery={setSearchQuery}
          progressFilter={progressFilter}
          setProgressFilter={setProgressFilter}
          showAdvancedFilters={showAdvancedFilters}
          setShowAdvancedFilters={setShowAdvancedFilters}
          historyLengthPast={history.past.length}
          historyLengthFuture={history.future.length}
          handleUndo={handleUndo}
          handleRedo={handleRedo}
          saveStatus={saveStatus}
          cartIdsCount={cartIds.length}
          copyJulesPrompt={copyJulesPrompt}
          copyCartIdsOnly={copyCartIdsOnly}
          setCartIds={setCartIds}
          setToast={setToast}
          minBlocker={minBlocker}
          setMinBlocker={setMinBlocker}
          minDifficulty={minDifficulty}
          setMinDifficulty={setMinDifficulty}
          minGenerality={minGenerality}
          setMinGenerality={setMinGenerality}
          statusCounts={statusCounts}
        />

        {/* Info Accordion */}
        <InfoAccordion />

        {/* Tier Sections */}
        {TIER_DEFINITIONS.map(({ tier, title }) => (
          <TierSection
            key={tier}
            tier={tier}
            title={title}
            tasks={tasksByTier[tier]}
            cartIds={cartIds}
            selectedTaskId={selectedTaskId}
            filterKey={deferredFilterKey}
            onCartClick={handleCartClick}
            onStatusChange={updateTaskStatus}
            onSelectTask={setSelectedTaskId}
            toggleTierCart={toggleTierCart}
          />
        ))}

        {filteredTasks.length === 0 && (
          <Card sx={{ p: 6, textAlign: 'center', bgcolor: '#1d1b20' }}>
            <Typography variant="body1" sx={{ color: 'text.secondary' }}>
              該当タスクが見つかりません。
            </Typography>
          </Card>
        )}

        {/* Global Toast / Snackbar Notification */}
        <Snackbar
          open={toast.open}
          autoHideDuration={4000}
          onClose={() => setToast({ ...toast, open: false })}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
          sx={{
            bottom: dockPosition === 'bottom' ? `${panelHeight + 16}px` : 24,
            ...(dockPosition === 'left' && { left: { md: '540px' } }),
            ...(dockPosition === 'right' && { right: { md: '540px' } }),
          }}
        >
          <Alert
            severity={toast.severity || 'success'}
            variant="filled"
            sx={{
              width: '100%',
              bgcolor: '#332d41',
              color: '#f4effa',
              borderRadius: 2,
              '& .MuiAlert-icon': { color: 'primary.light' },
            }}
            action={
              toast.actionText && toast.onAction ? (
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    toast.onAction?.();
                    setToast({ ...toast, open: false });
                  }}
                  sx={{ color: 'primary.light', fontWeight: 800 }}
                >
                  {toast.actionText}
                </Button>
              ) : null
            }
          >
            {toast.message}
          </Alert>
        </Snackbar>

        {/* Physical spacer for bottom dock */}
        {dockPosition === 'bottom' && (
          <Box sx={{ height: `${panelHeight}px`, width: '100%', mt: 4 }} />
        )}
          </Container>
        </Box>

        {/* Dynamic Task Detail Panel (Bottom / Left / Right Dock) */}
        <TaskDetailPanel
          task={selectedTask}
          dockPosition={dockPosition}
          onDockPositionChange={setDockPosition}
          onStatusChange={updateTaskStatus}
          onNotesChange={updateNotes}
          onClose={() => setSelectedTaskId(null)}
          onHeightChange={setPanelHeight}
        />
      </Box>
    </ThemeProvider>
  );
};

export default App;
