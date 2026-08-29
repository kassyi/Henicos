import React, { useState } from 'react';
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
  } = useRoadmap();

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

  return (
    <ThemeProvider theme={m3DarkTheme}>
      <CssBaseline />
      <Container
        maxWidth="xl"
        sx={{
          pt: 4,
          pb: `${panelHeight + 32}px`, // Dynamically perfectly synced with bottom panel height
          transition: 'padding-bottom 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
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
            exitingIds={exitingIds}
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
          sx={{ bottom: `${panelHeight + 16}px` }}
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

        {/* Fixed Bottom Task Detail Dock Panel with Height Observer */}
        <TaskDetailPanel
          task={selectedTask}
          onStatusChange={updateTaskStatus}
          onNotesChange={updateNotes}
          onHeightChange={setPanelHeight}
        />
        
        {/* Physical spacer to ensure enough scroll area at the bottom */}
        <Box sx={{ height: `${panelHeight}px`, width: '100%', mt: 4 }} />
      </Container>
    </ThemeProvider>
  );
};

export default App;
