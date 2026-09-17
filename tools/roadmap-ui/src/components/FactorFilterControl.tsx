import React from 'react';
import { Box, Typography, ToggleButton, ToggleButtonGroup } from '@mui/material';

export interface FilterOption<T> {
  val: T;
  label: string;
  subLabel: string;
  color: string;
  selectedBg: string;
  selectedText: string;
}

interface FactorFilterControlProps<T> {
  title: string;
  value: T;
  onChange: (val: T) => void;
  options: FilterOption<T>[];
}

export function FactorFilterControl<T extends number | string>({ title, value, onChange, options }: FactorFilterControlProps<T>) {
  const currentOpt = options.find((o) => o.val === value) || options[0];

  return (
    <Box sx={{ bgcolor: "#141218", p: 1.5, borderRadius: 2, border: "1px solid #332d41" }}>
      <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1 }}>
        <Typography variant="caption" sx={{ fontWeight: 700, color: "text.secondary" }}>
          {title.split(' : ').length === 2 ? (
            <>
              {title.split(' : ')[0]} : <strong>{title.split(' : ')[1]}</strong>
            </>
          ) : (
            title
          )}
        </Typography>
        <Typography
          variant="caption"
          sx={{ color: currentOpt.color, fontSize: "0.7rem", fontWeight: 700 }}
        >
          {currentOpt.subLabel}
        </Typography>
      </Box>
      <ToggleButtonGroup
        value={value}
        exclusive
        onChange={(_, val) => val !== null && onChange(val)}
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
        {options.map((opt) => (
          <ToggleButton
            key={String(opt.val)}
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
  );
}
