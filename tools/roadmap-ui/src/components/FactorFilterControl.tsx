import React from 'react';
import { Box, Typography, ToggleButtonGroup, ToggleButton } from '@mui/material';

export interface FactorOption {
  n: number;
  color: string;
  label: string;
}

interface FactorFilterControlProps {
  name: string;
  symbol: string;
  value: number;
  subLabel: string;
  options: FactorOption[];
  onChange: (val: number) => void;
}

export const FactorFilterControl: React.FC<FactorFilterControlProps> = ({
  name,
  symbol,
  value,
  subLabel,
  options,
  onChange,
}) => {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2 }}>
      <Box sx={{ minWidth: 90 }}>
        <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.primary', fontSize: '0.78rem' }}>
          {name} ({symbol}) &ge; <span style={{ color: '#d0bcff' }}>{value}</span>
        </Typography>
        <Typography variant="caption" sx={{ color: 'text.secondary', fontSize: '0.68rem', display: 'block', lineHeight: 1.1 }}>
          {subLabel}
        </Typography>
      </Box>
      <ToggleButtonGroup
        value={value}
        exclusive
        onChange={(_, val) => {
          if (val !== null) onChange(val);
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
        {options.map(({ n, color, label }) => (
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
              position: 'relative',
              '&.Mui-selected': { bgcolor: color, color: color === '#ffffff' ? '#141218' : '#141218', fontWeight: 800, '&:hover': { bgcolor: color } },
              '&:hover': { bgcolor: 'rgba(255, 255, 255, 0.08)' },
              '&::after': {
                content: '""',
                position: 'absolute',
                bottom: 2,
                left: '20%',
                right: '20%',
                height: 2,
                borderRadius: 1,
                bgcolor: color,
                opacity: value === n ? 0 : 0.6,
              },
            }}
          >
            {n}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
    </Box>
  );
};
