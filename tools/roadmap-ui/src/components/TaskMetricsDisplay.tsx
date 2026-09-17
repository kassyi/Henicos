import React from 'react';
import { Box } from '@mui/material';
import type { TaskMetrics } from '../types';

interface TaskMetricsDisplayProps {
  metrics: TaskMetrics;
  isVertical?: boolean;
}

export const TaskMetricsDisplay: React.FC<TaskMetricsDisplayProps> = ({ metrics, isVertical }) => {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', color: 'text.secondary', fontSize: isVertical ? '0.75rem' : '0.78rem', flexWrap: 'wrap', gap: isVertical ? 1.5 : 2 }}>
      <span>
        優先度スコア: <strong style={{ color: '#d0bcff', fontSize: isVertical ? '0.85rem' : '0.9rem' }}>{metrics.score.toFixed(1)}</strong>
      </span>
      <span style={{ opacity: 0.3 }}>|</span>
      <span>Blocker: <strong style={{ color: '#e6e0e9' }}>{metrics.blocker}</strong></span>
      <span>Difficulty: <strong style={{ color: '#e6e0e9' }}>{metrics.difficulty}</strong></span>
      <span>Generality: <strong style={{ color: '#e6e0e9' }}>{metrics.generality}</strong></span>
    </Box>
  );
};
