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
    Accordion,
    AccordionSummary,
    AccordionDetails,
    Chip,
    Fab,
    Badge,
    Tooltip,
} from "@mui/material";
import { ExpandMore as ExpandMoreIcon, HelpOutlined as HelpIcon, ContentCopy as CopyIcon } from "@mui/icons-material";
import { JulesIcon } from "./components/JulesIcon";
import { m3DarkTheme } from "./theme";
import { useRoadmap } from "./hooks/useRoadmap";
import { ControlsBar } from "./components/ControlsBar";
import { TierSection } from "./components/TierSection";

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
        completionFilter,
        setCompletionFilter,
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
                <Box sx={{ display: "flex", justifyContent: "center", alignItems: "center", height: "100vh" }}>
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
                <Box sx={{ textAlign: "center", mb: 4 }}>
                    <Typography variant="h4" component="h1" sx={{ color: "primary.main", mb: 1 }}>
                        Henicos Roadmap Manager
                    </Typography>
                    <Typography variant="body1" sx={{ color: "text.secondary" }}>
                        インタラクティブ進捗管理システム
                    </Typography>
                </Box>

                <ControlsBar
                    searchQuery={searchQuery}
                    setSearchQuery={setSearchQuery}
                    completionFilter={completionFilter}
                    setCompletionFilter={setCompletionFilter}
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
                <Accordion sx={{ mb: 4, bgcolor: "#1d1b20", border: "1px solid #49454f" }}>
                    <AccordionSummary
                        expandIcon={<ExpandMoreIcon />}
                        sx={{ minHeight: 48, "& .MuiAccordionSummary-content": { my: 1.5 } }}
                    >
                        <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                            <HelpIcon fontSize="small" sx={{ color: "primary.light" }} />
                            <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
                                スコアと用語の解説（クリックで展開）
                            </Typography>
                        </Box>
                    </AccordionSummary>
                    <AccordionDetails sx={{ borderTop: "1px solid #49454f", bgcolor: "#141218", p: 3 }}>
                        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr 1fr" }, gap: 4 }}>
                            {/* 因子 */}
                            <Box>
                                <Typography
                                    variant="caption"
                                    sx={{ fontWeight: 700, color: "primary.light", display: "block", mb: 1 }}
                                >
                                    【因子 (Factors)】
                                </Typography>
                                <Box sx={{ display: "flex", flexDirection: "column", gap: 1, fontSize: "0.8rem" }}>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                                        <Chip
                                            label="依存度 (B)"
                                            size="small"
                                            sx={{
                                                minWidth: 80,
                                                fontWeight: 700,
                                                bgcolor: "#332d41",
                                                color: "#e8def8",
                                                fontSize: "0.72rem",
                                            }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            <strong>1</strong> (自己完結) &rarr; <strong>3</strong> (一部依存) &rarr;{" "}
                                            <strong>5</strong> (完全ブロック/最優先)
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                                        <Chip
                                            label="難易度 (D)"
                                            size="small"
                                            sx={{
                                                minWidth: 80,
                                                fontWeight: 700,
                                                bgcolor: "#332d41",
                                                color: "#e8def8",
                                                fontSize: "0.72rem",
                                            }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            <strong>1</strong> (極めて簡単) &rarr; <strong>3</strong> (普通) &rarr;{" "}
                                            <strong>5</strong> (最難関/大設計)
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                                        <Chip
                                            label="汎用性 (G)"
                                            size="small"
                                            sx={{
                                                minWidth: 80,
                                                fontWeight: 700,
                                                bgcolor: "#332d41",
                                                color: "#e8def8",
                                                fontSize: "0.72rem",
                                            }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            <strong>1</strong> (ニッチ/稀) &rarr; <strong>3</strong> (通常) &rarr;{" "}
                                            <strong>5</strong> (常時使われる)
                                        </Typography>
                                    </Box>
                                </Box>
                            </Box>

                            {/* ステータス */}
                            <Box>
                                <Typography
                                    variant="caption"
                                    sx={{ fontWeight: 700, color: "primary.light", display: "block", mb: 1 }}
                                >
                                    【ステータス】
                                </Typography>
                                <Box sx={{ display: "flex", flexDirection: "column", gap: 0.8, fontSize: "0.8rem" }}>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                                        <Chip
                                            label="未完"
                                            size="small"
                                            sx={{
                                                minWidth: 70,
                                                fontWeight: 600,
                                                bgcolor: "#141218",
                                                color: "text.secondary",
                                                border: "1px solid #49454f",
                                                fontSize: "0.7rem",
                                            }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            未着手
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                                        <Chip
                                            label="🛠️ 実装"
                                            size="small"
                                            sx={{
                                                minWidth: 70,
                                                fontWeight: 700,
                                                bgcolor: "#2e5b44",
                                                color: "#c4eed0",
                                                fontSize: "0.7rem",
                                            }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            実装・テスト完了
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                                        <Chip
                                            label="⚡ パフォ"
                                            size="small"
                                            sx={{
                                                minWidth: 70,
                                                fontWeight: 700,
                                                bgcolor: "#5c256b",
                                                color: "#f3e5f5",
                                                fontSize: "0.7rem",
                                            }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            GPU最適化・チューニング完了
                                        </Typography>
                                    </Box>
                                </Box>
                            </Box>

                            {/* ドメイン */}
                            <Box>
                                <Typography
                                    variant="caption"
                                    sx={{ fontWeight: 700, color: "primary.light", display: "block", mb: 1 }}
                                >
                                    【ドメイン】
                                </Typography>
                                <Box
                                    sx={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 1, fontSize: "0.8rem" }}
                                >
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 0.8 }}>
                                        <Chip
                                            label="SYN"
                                            size="small"
                                            variant="outlined"
                                            sx={{ fontWeight: 700, fontSize: "0.72rem", borderColor: "#49454f" }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            構文 (Syntax)
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 0.8 }}>
                                        <Chip
                                            label="GBI"
                                            size="small"
                                            variant="outlined"
                                            sx={{ fontWeight: 700, fontSize: "0.72rem", borderColor: "#49454f" }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            GPU基盤 (Builtins)
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 0.8 }}>
                                        <Chip
                                            label="GSH"
                                            size="small"
                                            variant="outlined"
                                            sx={{ fontWeight: 700, fontSize: "0.72rem", borderColor: "#49454f" }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            トランスパイラ (Core)
                                        </Typography>
                                    </Box>
                                    <Box sx={{ display: "flex", alignItems: "center", gap: 0.8 }}>
                                        <Chip
                                            label="GEN"
                                            size="small"
                                            variant="outlined"
                                            sx={{ fontWeight: 700, fontSize: "0.72rem", borderColor: "#49454f" }}
                                        />
                                        <Typography variant="caption" sx={{ color: "text.secondary" }}>
                                            ジェネレータ基盤
                                        </Typography>
                                    </Box>
                                </Box>
                            </Box>
                        </Box>
                    </AccordionDetails>
                </Accordion>

                {/* Tier 一覧 */}
                <TierSection
                    tier={1}
                    title="Tier 1 (最優先 / Score >= 36)"
                    tasks={tasksByTier[1]}
                    cartIds={cartIds}
                    exitingIds={exitingIds}
                    onCartClick={handleCartClick}
                    onStatusChange={updateTaskStatus}
                    onNotesChange={updateNotes}
                    toggleTierCart={toggleTierCart}
                />
                <TierSection
                    tier={2}
                    title="Tier 2 (優先 / Score >= 28)"
                    tasks={tasksByTier[2]}
                    cartIds={cartIds}
                    exitingIds={exitingIds}
                    onCartClick={handleCartClick}
                    onStatusChange={updateTaskStatus}
                    onNotesChange={updateNotes}
                    toggleTierCart={toggleTierCart}
                />
                <TierSection
                    tier={3}
                    title="Tier 3 (通常タスク / Score >= 20)"
                    tasks={tasksByTier[3]}
                    cartIds={cartIds}
                    exitingIds={exitingIds}
                    onCartClick={handleCartClick}
                    onStatusChange={updateTaskStatus}
                    onNotesChange={updateNotes}
                    toggleTierCart={toggleTierCart}
                />
                <TierSection
                    tier={4}
                    title="Tier 4 (後回し / Score < 20)"
                    tasks={tasksByTier[4]}
                    cartIds={cartIds}
                    exitingIds={exitingIds}
                    onCartClick={handleCartClick}
                    onStatusChange={updateTaskStatus}
                    onNotesChange={updateNotes}
                    toggleTierCart={toggleTierCart}
                />

                {filteredTasks.length === 0 && (
                    <Card sx={{ p: 6, textAlign: "center", bgcolor: "#1d1b20" }}>
                        <Typography variant="body1" sx={{ color: "text.secondary" }}>
                            該当タスクが見つかりません。
                        </Typography>
                    </Card>
                )}

                {/* AI Cart FAB (Direct 1-Click Prompt Copy) */}
                {cartIds.length > 0 && (
                    <Box sx={{ position: "fixed", bottom: 32, right: 32, display: "flex", flexDirection: "column", gap: 1.5, zIndex: 1000 }}>
                        <Tooltip title="タスクIDのみコピー" placement="left">
                            <Fab
                                color="secondary"
                                aria-label="Copy Task IDs"
                                onClick={copyCartIdsOnly}
                                size="small"
                                sx={{ borderRadius: 2, alignSelf: "center", bgcolor: '#4a4458', color: '#e8def8', '&:hover': { bgcolor: '#332d41' } }}
                            >
                                <CopyIcon fontSize="small" />
                            </Fab>
                        </Tooltip>
                        <Tooltip title="Julesプロンプト生成" placement="left">
                            <Fab
                                color="primary"
                                aria-label="Generate Jules Prompt"
                                onClick={copyJulesPrompt}
                                sx={{ borderRadius: 2 }}
                            >
                                <Badge badgeContent={cartIds.length} color="error">
                                    <JulesIcon />
                                </Badge>
                            </Fab>
                        </Tooltip>
                    </Box>
                )}

                <Snackbar
                    open={toast.open}
                    autoHideDuration={4500}
                    onClose={() => setToast((prev) => ({ ...prev, open: false }))}
                    anchorOrigin={{ vertical: "bottom", horizontal: "center" }}
                >
                    <Alert
                        severity={toast.severity || "info"}
                        variant="filled"
                        sx={{
                            borderRadius: 2,
                            fontWeight: 600,
                            bgcolor: "#2d2b33",
                            color: "#f4effa",
                            border: "1px solid #49454f",
                            boxShadow: "0 6px 24px rgba(0,0,0,0.6)",
                            display: "flex",
                            alignItems: "center",
                            py: 0.5,
                            px: 2,
                            "& .MuiAlert-icon": { color: "primary.light", my: "auto" },
                            "& .MuiAlert-message": { py: 0.5, fontSize: "0.85rem" },
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
                                        color: "#d0bcff",
                                        fontWeight: 800,
                                        textTransform: "none",
                                        fontSize: "0.82rem",
                                        px: 1.5,
                                        py: 0.3,
                                        borderRadius: 1.5,
                                        bgcolor: "rgba(208, 188, 255, 0.08)",
                                        "&:hover": { bgcolor: "rgba(208, 188, 255, 0.2)" },
                                    }}
                                >
                                    {toast.actionText || "元に戻す"}
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
