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
    Collapse,
    Chip,
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
import { FactorFilterControl } from "./FactorFilterControl";
import type { FilterOption } from "./FactorFilterControl";

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
        val: "planned",
        label: "計画済",
        subLabel: "計画済",
        color: "#64b5f6",
        selectedBg: "#1565c0",
        selectedText: "#e3f2fd",
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
        val: "rejected",
        label: "却下済",
        subLabel: "対象外・統合済",
        color: "#ef9a9a",
        selectedBg: "#c62828",
        selectedText: "#ffebee",
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
        planned: number;
        implemented_plus: number;
        tuned: number;
        rejected: number;
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
                            <Box
                                sx={{
                                    display: "flex",
                                    flexWrap: "wrap",
                                    gap: 1,
                                    pt: 0.5,
                                }}
                            >
                                {PROGRESS_OPTIONS.map((opt) => {
                                    const count = statusCounts ? statusCounts[opt.val] : undefined;
                                    const isSelected = progressFilter === opt.val;
                                    return (
                                        <Chip
                                            key={opt.val}
                                            label={`${opt.label} ${count !== undefined ? `(${count})` : ""}`}
                                            onClick={() => setProgressFilter(opt.val)}
                                            size="small"
                                            sx={{
                                                bgcolor: isSelected ? opt.selectedBg : "rgba(255, 255, 255, 0.05)",
                                                color: isSelected ? opt.selectedText : "text.secondary",
                                                border: "1px solid",
                                                borderColor: isSelected ? opt.selectedBg : "rgba(255, 255, 255, 0.1)",
                                                fontWeight: isSelected ? 800 : 500,
                                                fontSize: "0.75rem",
                                                transition: "all 0.2s ease",
                                                "&:hover": {
                                                    bgcolor: isSelected ? opt.selectedBg : "rgba(255, 255, 255, 0.1)",
                                                    borderColor: isSelected ? opt.selectedBg : "rgba(255, 255, 255, 0.2)",
                                                },
                                            }}
                                        />
                                    );
                                })}
                            </Box>
                        </Box>

                        {/* 2. Blocker Filter Card */}
                        <FactorFilterControl
                            title={`Blocker (依存度) : ${minBlocker} 以上`}
                            value={minBlocker}
                            onChange={(val) => setMinBlocker(val as number)}
                            options={BLOCKER_OPTIONS}
                        />

                        {/* 3. Difficulty Filter Card */}
                        <FactorFilterControl
                            title={`Difficulty (難易度) : ${minDifficulty} 以上`}
                            value={minDifficulty}
                            onChange={(val) => setMinDifficulty(val as number)}
                            options={DIFFICULTY_OPTIONS}
                        />

                        {/* 4. Generality Filter Card */}
                        <FactorFilterControl
                            title={`Generality (汎用性) : ${minGenerality} 以上`}
                            value={minGenerality}
                            onChange={(val) => setMinGenerality(val as number)}
                            options={GENERALITY_OPTIONS}
                        />
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
