import {
  ThemeProvider,
  CssBaseline,
  Container,
  Box,
  Typography,
  Card,
  CircularProgress,
  Snackbar,
  Alert,
  Button,
} from '@mui/material';
import { m3DarkTheme } from './theme';
import { useRoadmap } from './hooks/useRoadmap';
import { ControlsBar } from './components/ControlsBar';
import { TierSection } from './components/TierSection';
import { InfoAccordion } from './components/InfoAccordion';
import { CartFab } from './components/CartFab';

const TIER_DEFINITIONS = [
  { tier: 1, title: 'Tier 1 (最優先 / Score >= 36)' },
  { tier: 2, title: 'Tier 2 (優先 / Score >= 28)' },
  { tier: 3, title: 'Tier 3 (通常タスク / Score >= 20)' },
  { tier: 4, title: 'Tier 4 (後回し / Score < 20)' },
] as const;

export default function App() {
  const {
    loading,
    saveStatus,
    exitingIds,
    searchQuery,
    setSearchQuery,
    minBlocker,
    setMinBlocker,
    minDifficulty,
    setMinDifficulty,
    minGenerality,
    setMinGenerality,
    showAdvancedFilters,
    setShowAdvancedFilters,
    statusFilters,
    setStatusFilters,
    toast,
    setToast,
    cartIds,
    setCartIds,
    handleUndo,
    handleRedo,
    history,
    updateTaskStatus,
    handleCartClick,
    toggleTierCart,
    copyJulesPrompt,
    copyCartIdsOnly,
    updateNotes,
    filteredTasks,
    tasksByTier,
  } = useRoadmap();

  if (loading) {
    return (
      <ThemeProvider theme={m3DarkTheme}>
        <CssBaseline />
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
          <CircularProgress color="primary" />
        </Box>
      </ThemeProvider>
    );
  }

  return (
    <ThemeProvider theme={m3DarkTheme}>
      <CssBaseline />
      <Container maxWidth="xl" sx={{ py: 4 }}>
        {/* Top App Header */}
        <Box sx={{ textAlign: 'center', mb: 4 }}>
          <Typography variant="h4" component="h1" sx={{ color: 'primary.main', mb: 1 }}>
            Henicos Roadmap Manager
          </Typography>
          <Typography variant="body1" sx={{ color: 'text.secondary' }}>
            インタラクティブ進捗管理システム
          </Typography>
        </Box>

        <ControlsBar
          searchQuery={searchQuery}
          setSearchQuery={setSearchQuery}
          statusFilters={statusFilters}
          setStatusFilters={setStatusFilters}
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
            exitingIds={exitingIds}
            onCartClick={handleCartClick}
            onStatusChange={updateTaskStatus}
            onNotesChange={updateNotes}
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

        {/* AI Cart FAB Cluster */}
        <CartFab
          count={cartIds.length}
          onCopyJulesPrompt={copyJulesPrompt}
          onCopyIdsOnly={copyCartIdsOnly}
        />

        {/* Toast / Notification Alert */}
        <Snackbar
          open={toast.open}
          autoHideDuration={4500}
          onClose={() => setToast((prev) => ({ ...prev, open: false }))}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        >
          <Alert
            severity={toast.severity || 'info'}
            variant="filled"
            sx={{
              borderRadius: 2,
              fontWeight: 600,
              bgcolor: '#2d2b33',
              color: '#f4effa',
              border: '1px solid #49454f',
              boxShadow: '0 6px 24px rgba(0,0,0,0.6)',
              display: 'flex',
              alignItems: 'center',
              py: 0.5,
              px: 2,
              '& .MuiAlert-icon': { color: 'primary.light', my: 'auto' },
              '& .MuiAlert-message': { py: 0.5, fontSize: '0.85rem' },
            }}
            action={
              toast.onAction ? (
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    toast.onAction?.();
                    setToast((prev) => ({ ...prev, open: false }));
                  }}
                  sx={{
                    color: '#d0bcff',
                    fontWeight: 800,
                    textTransform: 'none',
                    fontSize: '0.82rem',
                    px: 1.5,
                    py: 0.3,
                    borderRadius: 1.5,
                    bgcolor: 'rgba(208, 188, 255, 0.08)',
                    '&:hover': { bgcolor: 'rgba(208, 188, 255, 0.2)' },
                  }}
                >
                  {toast.actionText || '元に戻す'}
                </Button>
              ) : undefined
            }
          >
            {toast.message}
          </Alert>
        </Snackbar>
      </Container>
    </ThemeProvider>
  );
}
