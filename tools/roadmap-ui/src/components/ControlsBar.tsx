import React from "react";
import {
    Box,
    Card,
    TextField,
    InputAdornment,
    IconButton,
    Button,
    Badge,
    Tooltip,
    Typography,
    ToggleButton,
    ToggleButtonGroup,
    Collapse,
} from "@mui/material";
import {
    Search as SearchIcon,
    FilterList as FilterListIcon,
    ContentCopy as ContentCopyIcon,
    Undo as UndoIcon,
    Redo as RedoIcon,
    RestartAlt as ResetIcon,
    CheckCircle as SavedIcon,
    Sync as SyncingIcon,
    Error as ErrorIcon,
} from "@mui/icons-material";
import type { ProgressFilter } from "../types";
import { JulesIcon } from "./JulesIcon";

interface FilterOption<T> {
    val: T;
    label: string;
    subLabel: string;
    color: string;
    selectedBg: string;
    selectedText: string;
}

const PROGRESS_OPTIONS: FilterOption<ProgressFilter>[] = [
    {
        val: "incomplete",
        label: "未完了",
        subLabel: "未着手・未実装",
        color: "#90a4ae",
        selectedBg: "#455a64",
        selectedText: "#eceff1",
    },
    {
        val: "implemented_plus",
        label: "実装以上",
        subLabel: "実装＋パフォ完了",
        color: "#81c784",
        selectedBg: "#2e7d32",
        selectedText: "#e8f5e9",
    },
    {
        val: "tuned",
        label: "パフォ済",
        subLabel: "パフォ済",
        color: "#ce93d8",
        selectedBg: "#7b1fa2",
        selectedText: "#f3e5f5",
    },
    {
        val: "all",
        label: "すべて",
        subLabel: "全件表示",
        color: "#64b5f6",
        selectedBg: "#1565c0",
        selectedText: "#e3f2fd",
    },
];

const BLOCKER_OPTIONS: FilterOption<number>[] = [
    { val: 1, label: "1", subLabel: "小 (自己完結)", color: "#c8e6c9", selectedBg: "#2e7d32", selectedText: "#ffffff" },
    { val: 2, label: "2", subLabel: "軽微", color: "#a5d6a7", selectedBg: "#388e3c", selectedText: "#ffffff" },
    { val: 3, label: "3", subLabel: "通常", color: "#81c784", selectedBg: "#43a047", selectedText: "#ffffff" },
    { val: 4, label: "4", subLabel: "重要", color: "#66bb6a", selectedBg: "#4caf50", selectedText: "#ffffff" },
    {
        val: 5,
        label: "5",
        subLabel: "大 (完全ブロック)",
        color: "#4caf50",
        selectedBg: "#66bb6a",
        selectedText: "#141218",
    },
];

const DIFFICULTY_OPTIONS: FilterOption<number>[] = [
    { val: 1, label: "1", subLabel: "極めて簡単", color: "#ffe082", selectedBg: "#f57f17", selectedText: "#ffffff" },
    { val: 2, label: "2", subLabel: "易しい", color: "#ffd54f", selectedBg: "#f57c00", selectedText: "#ffffff" },
    { val: 3, label: "3", subLabel: "普通", color: "#ffb74d", selectedBg: "#ef6c00", selectedText: "#ffffff" },
    { val: 4, label: "4", subLabel: "難関", color: "#ff7043", selectedBg: "#e65100", selectedText: "#ffffff" },
    { val: 5, label: "5", subLabel: "最難関/大設計", color: "#ef5350", selectedBg: "#c62828", selectedText: "#ffffff" },
];

const GENERALITY_OPTIONS: FilterOption<number>[] = [
    { val: 1, label: "1", subLabel: "ニッチ/稀", color: "#90caf9", selectedBg: "#1565c0", selectedText: "#ffffff" },
    { val: 2, label: "2", subLabel: "限定的", color: "#42a5f5", selectedBg: "#1976d2", selectedText: "#ffffff" },
    { val: 3, label: "3", subLabel: "通常", color: "#1e88e5", selectedBg: "#1e88e5", selectedText: "#ffffff" },
    { val: 4, label: "4", subLabel: "頻繁に利用", color: "#1565c0", selectedBg: "#0277bd", selectedText: "#ffffff" },
    {
        val: 5,
        label: "5",
        subLabel: "常時使われる/基盤",
        color: "#0d47a1",
        selectedBg: "#01579b",
        selectedText: "#ffffff",
    },
];

interface ControlsBarProps {
    searchQuery: string;
    setSearchQuery: (val: string) => void;
    progressFilter: ProgressFilter;
    setProgressFilter: (val: ProgressFilter) => void;
    showAdvancedFilters: boolean;
    setShowAdvancedFilters: (val: boolean) => void;
    historyLengthPast: number;
    historyLengthFuture: number;
    handleUndo: () => void;
    handleRedo: () => void;
    saveStatus: "saved" | "saving" | "error";
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
    statusCounts?: {
        all: number;
        incomplete: number;
        implemented_plus: number;
        tuned: number;
    };
}

export const ControlsBar: React.FC<ControlsBarProps> = ({
    searchQuery,
    setSearchQuery,
    progressFilter,
    setProgressFilter,
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
    statusCounts,
}) => {
    const currentProgressOpt = PROGRESS_OPTIONS.find((p) => p.val === progressFilter) || PROGRESS_OPTIONS[0];
    const currentBlockerOpt = BLOCKER_OPTIONS.find((b) => b.val === minBlocker) || BLOCKER_OPTIONS[0];
    const currentDifficultyOpt = DIFFICULTY_OPTIONS.find((d) => d.val === minDifficulty) || DIFFICULTY_OPTIONS[0];
    const currentGeneralityOpt = GENERALITY_OPTIONS.find((g) => g.val === minGenerality) || GENERALITY_OPTIONS[0];

    const hasActiveFilters = progressFilter !== "all" || minBlocker > 1 || minDifficulty > 1 || minGenerality > 1;

    return (
        <Card
            sx={{
                p: 2.5,
                mb: 2.5,
                bgcolor: "#211f26",
                borderRadius: 2,
                border: "1px solid #49454f",
                "&:hover": {
                    transform: "none",
                    borderColor: "#49454f",
                    bgcolor: "#211f26",
                },
            }}
        >
            {/* Row 1: Streamlined Search Bar & Quick Action Toolbar */}
            <Box
                sx={{
                    display: "flex",
                    flexWrap: "wrap",
                    gap: 2,
                    alignItems: "center",
                    justifyContent: "space-between",
                }}
            >
                {/* Left Side: Spacious Search Box */}
                <Box sx={{ flex: 1, minWidth: { xs: "100%", sm: 280, md: 360 } }}>
                    <TextField
                        placeholder="タスクID、タイトル、メモで検索..."
                        value={searchQuery}
                        onChange={(e) => setSearchQuery(e.target.value)}
                        size="small"
                        fullWidth
                        sx={{
                            bgcolor: "#141218",
                            borderRadius: 2,
                            "& .MuiOutlinedInput-root": {
                                fontSize: "0.88rem",
                                "& fieldset": { borderColor: "#49454f" },
                                "&:hover fieldset": { borderColor: "primary.main" },
                            },
                        }}
                        slotProps={{
                            input: {
                                startAdornment: (
                                    <InputAdornment position="start">
                                        <SearchIcon sx={{ color: "text.secondary" }} />
                                    </InputAdornment>
                                ),
                            },
                        }}
                    />
                </Box>

                {/* Right Side: Action Buttons & Filter Toggle */}
                <Box sx={{ display: "flex", gap: 1, alignItems: "center", flexWrap: "wrap" }}>
                    {/* Undo / Redo */}
                    <Tooltip title="元に戻す (Ctrl+Z)">
                        <span>
                            <IconButton
                                size="small"
                                onClick={handleUndo}
                                disabled={historyLengthPast === 0}
                                sx={{ color: "text.secondary" }}
                            >
                                <UndoIcon fontSize="small" />
                            </IconButton>
                        </span>
                    </Tooltip>

                    <Tooltip title="やり直す (Ctrl+Y)">
                        <span>
                            <IconButton
                                size="small"
                                onClick={handleRedo}
                                disabled={historyLengthFuture === 0}
                                sx={{ color: "text.secondary" }}
                            >
                                <RedoIcon fontSize="small" />
                            </IconButton>
                        </span>
                    </Tooltip>

                    {/* Save Status Icon */}
                    <Box sx={{ display: "flex", alignItems: "center", ml: 0.5, mr: 0.5 }}>
                        {saveStatus === "saved" && (
                            <Tooltip title="保存済み">
                                <SavedIcon sx={{ fontSize: 18, color: "#c4eed0" }} />
                            </Tooltip>
                        )}
                        {saveStatus === "saving" && (
                            <Tooltip title="自動保存中...">
                                <SyncingIcon
                                    sx={{
                                        fontSize: 18,
                                        color: "primary.main",
                                        animation: "spin 1.5s linear infinite",
                                        "@keyframes spin": { "100%": { transform: "rotate(360deg)" } },
                                    }}
                                />
                            </Tooltip>
                        )}
                        {saveStatus === "error" && (
                            <Tooltip title="保存エラー">
                                <ErrorIcon sx={{ fontSize: 18, color: "#f2b8b5" }} />
                            </Tooltip>
                        )}
                    </Box>

                    {/* AI Task Cart & Prompt Copy Actions */}
                    <Button
                        variant="contained"
                        color="primary"
                        size="small"
                        startIcon={
                            <Badge badgeContent={cartIdsCount} color="error" max={99}>
                                <JulesIcon sx={{ fontSize: 20 }} />
                            </Badge>
                        }
                        onClick={copyJulesPrompt}
                        disabled={cartIdsCount === 0}
                        sx={{
                            fontWeight: 700,
                            fontSize: "0.78rem",
                            borderRadius: 2,
                            px: 1.8,
                            height: 34,
                        }}
                    >
                        Jules用プロンプトコピー
                    </Button>

                    <Button
                        variant="outlined"
                        size="small"
                        startIcon={<ContentCopyIcon fontSize="small" />}
                        onClick={copyCartIdsOnly}
                        disabled={cartIdsCount === 0}
                        sx={{
                            color: "text.primary",
                            borderColor: "#49454f",
                            fontSize: "0.75rem",
                            height: 34,
                            borderRadius: 2,
                            "&:hover": { borderColor: "primary.main" },
                        }}
                    >
                        ID一覧
                    </Button>

                    {cartIdsCount > 0 && (
                        <Button
                            variant="text"
                            size="small"
                            onClick={() => {
                                setCartIds([]);
                                setToast({ open: true, message: "AIカートをクリアしました" });
                            }}
                            sx={{ color: "text.secondary", fontSize: "0.75rem", height: 34 }}
                        >
                            カート解除
                        </Button>
                    )}

                    {/* Filter Panel Toggle Button */}
                    <Button
                        variant={showAdvancedFilters ? "contained" : "outlined"}
                        color={showAdvancedFilters ? "secondary" : "inherit"}
                        size="small"
                        startIcon={<FilterListIcon />}
                        onClick={() => setShowAdvancedFilters(!showAdvancedFilters)}
                        sx={{
                            fontSize: "0.75rem",
                            height: 34,
                            borderRadius: 2,
                            borderColor: "#49454f",
                            fontWeight: 700,
                        }}
                    >
                        フィルター
                    </Button>
                </Box>
            </Box>

            {/* Row 2: Unified Filter Controls (Progress + Metrics) */}
            <Collapse in={showAdvancedFilters}>
                <Box
                    sx={{
                        mt: 2.5,
                        pt: 2,
                        borderTop: "1px solid rgba(255, 255, 255, 0.08)",
                        display: "flex",
                        flexDirection: "column",
                        gap: 2,
                    }}
                >
                    <Box
                        sx={{
                            display: "grid",
                            gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr", lg: "repeat(4, 1fr)" },
                            gap: 2,
                        }}
                    >
                        {/* 1. Progress Filter Card */}
                        <Box sx={{ bgcolor: "#141218", p: 1.5, borderRadius: 2, border: "1px solid #332d41" }}>
                            <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1 }}>
                                <Typography variant="caption" sx={{ fontWeight: 700, color: "text.secondary" }}>
                                    進捗ステージ
                                </Typography>
                                <Typography
                                    variant="caption"
                                    sx={{ color: currentProgressOpt.color, fontSize: "0.7rem", fontWeight: 700 }}
                                >
                                    {currentProgressOpt.subLabel}
                                </Typography>
                            </Box>
                            <ToggleButtonGroup
                                value={progressFilter}
                                exclusive
                                onChange={(_, val: ProgressFilter | null) => val && setProgressFilter(val)}
                                fullWidth
                                size="small"
                                sx={{
                                    bgcolor: "#1e1a24",
                                    "& .MuiToggleButton-root": {
                                        py: 0.4,
                                        px: 0.6,
                                        fontSize: "0.72rem",
                                        color: "text.secondary",
                                        borderColor: "#332d41",
                                        fontWeight: 600,
                                        whiteSpace: "nowrap",
                                    },
                                }}
                            >
                                {PROGRESS_OPTIONS.map((opt) => {
                                    const count = statusCounts ? statusCounts[opt.val] : undefined;
                                    return (
                                        <ToggleButton
                                            key={opt.val}
                                            value={opt.val}
                                            title={opt.subLabel}
                                            sx={{
                                                "&.Mui-selected": {
                                                    bgcolor: `${opt.selectedBg} !important`,
                                                    color: `${opt.selectedText} !important`,
                                                    fontWeight: 800,
                                                },
                                            }}
                                        >
                                            {opt.label} {count !== undefined ? `(${count})` : ""}
                                        </ToggleButton>
                                    );
                                })}
                            </ToggleButtonGroup>
                        </Box>

                        {/* 2. Blocker Filter Card */}
                        <Box sx={{ bgcolor: "#141218", p: 1.5, borderRadius: 2, border: "1px solid #332d41" }}>
                            <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1 }}>
                                <Typography variant="caption" sx={{ fontWeight: 700, color: "text.secondary" }}>
                                    Blocker (依存度) : <strong>{minBlocker} 以上</strong>
                                </Typography>
                                <Typography
                                    variant="caption"
                                    sx={{ color: currentBlockerOpt.color, fontSize: "0.7rem", fontWeight: 700 }}
                                >
                                    {currentBlockerOpt.subLabel}
                                </Typography>
                            </Box>
                            <ToggleButtonGroup
                                value={minBlocker}
                                exclusive
                                onChange={(_, val) => val !== null && setMinBlocker(val)}
                                fullWidth
                                size="small"
                                sx={{
                                    bgcolor: "#1e1a24",
                                    "& .MuiToggleButton-root": {
                                        py: 0.4,
                                        fontSize: "0.75rem",
                                        color: "text.secondary",
                                        borderColor: "#332d41",
                                    },
                                }}
                            >
                                {BLOCKER_OPTIONS.map((opt) => (
                                    <ToggleButton
                                        key={opt.val}
                                        value={opt.val}
                                        title={opt.subLabel}
                                        sx={{
                                            "&.Mui-selected": {
                                                bgcolor: `${opt.selectedBg} !important`,
                                                color: `${opt.selectedText} !important`,
                                                fontWeight: 800,
                                            },
                                        }}
                                    >
                                        {opt.label}
                                    </ToggleButton>
                                ))}
                            </ToggleButtonGroup>
                        </Box>

                        {/* 3. Difficulty Filter Card */}
                        <Box sx={{ bgcolor: "#141218", p: 1.5, borderRadius: 2, border: "1px solid #332d41" }}>
                            <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1 }}>
                                <Typography variant="caption" sx={{ fontWeight: 700, color: "text.secondary" }}>
                                    Difficulty (難易度) : <strong>{minDifficulty} 以上</strong>
                                </Typography>
                                <Typography
                                    variant="caption"
                                    sx={{ color: currentDifficultyOpt.color, fontSize: "0.7rem", fontWeight: 700 }}
                                >
                                    {currentDifficultyOpt.subLabel}
                                </Typography>
                            </Box>
                            <ToggleButtonGroup
                                value={minDifficulty}
                                exclusive
                                onChange={(_, val) => val !== null && setMinDifficulty(val)}
                                fullWidth
                                size="small"
                                sx={{
                                    bgcolor: "#1e1a24",
                                    "& .MuiToggleButton-root": {
                                        py: 0.4,
                                        fontSize: "0.75rem",
                                        color: "text.secondary",
                                        borderColor: "#332d41",
                                    },
                                }}
                            >
                                {DIFFICULTY_OPTIONS.map((opt) => (
                                    <ToggleButton
                                        key={opt.val}
                                        value={opt.val}
                                        title={opt.subLabel}
                                        sx={{
                                            "&.Mui-selected": {
                                                bgcolor: `${opt.selectedBg} !important`,
                                                color: `${opt.selectedText} !important`,
                                                fontWeight: 800,
                                            },
                                        }}
                                    >
                                        {opt.label}
                                    </ToggleButton>
                                ))}
                            </ToggleButtonGroup>
                        </Box>

                        {/* 4. Generality Filter Card */}
                        <Box sx={{ bgcolor: "#141218", p: 1.5, borderRadius: 2, border: "1px solid #332d41" }}>
                            <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1 }}>
                                <Typography variant="caption" sx={{ fontWeight: 700, color: "text.secondary" }}>
                                    Generality (汎用性) : <strong>{minGenerality} 以上</strong>
                                </Typography>
                                <Typography
                                    variant="caption"
                                    sx={{ color: currentGeneralityOpt.color, fontSize: "0.7rem", fontWeight: 700 }}
                                >
                                    {currentGeneralityOpt.subLabel}
                                </Typography>
                            </Box>
                            <ToggleButtonGroup
                                value={minGenerality}
                                exclusive
                                onChange={(_, val) => val !== null && setMinGenerality(val)}
                                fullWidth
                                size="small"
                                sx={{
                                    bgcolor: "#1e1a24",
                                    "& .MuiToggleButton-root": {
                                        py: 0.4,
                                        fontSize: "0.75rem",
                                        color: "text.secondary",
                                        borderColor: "#332d41",
                                    },
                                }}
                            >
                                {GENERALITY_OPTIONS.map((opt) => (
                                    <ToggleButton
                                        key={opt.val}
                                        value={opt.val}
                                        title={opt.subLabel}
                                        sx={{
                                            "&.Mui-selected": {
                                                bgcolor: `${opt.selectedBg} !important`,
                                                color: `${opt.selectedText} !important`,
                                                fontWeight: 800,
                                            },
                                        }}
                                    >
                                        {opt.label}
                                    </ToggleButton>
                                ))}
                            </ToggleButtonGroup>
                        </Box>
                    </Box>

                    {/* Reset Filters Control */}
                    {hasActiveFilters && (
                        <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                            <Button
                                variant="text"
                                size="small"
                                startIcon={<ResetIcon />}
                                onClick={() => {
                                    setProgressFilter("all");
                                    setMinBlocker(1);
                                    setMinDifficulty(1);
                                    setMinGenerality(1);
                                }}
                                sx={{ color: "text.secondary", fontSize: "0.72rem" }}
                            >
                                フィルター条件を初期値にリセット
                            </Button>
                        </Box>
                    )}
                </Box>
            </Collapse>
        </Card>
    );
};
