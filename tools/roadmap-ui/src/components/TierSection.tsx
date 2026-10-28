import React from 'react';
import { Box, Typography, Button, Chip } from '@mui/material';
import { AddShoppingCart as AddCartIcon, RemoveShoppingCart as RemoveCartIcon } from '@mui/icons-material';
import type { RoadmapTask, TaskStatus } from '../types';
import { TaskCard } from './TaskCard';

interface TierSectionProps {
  tier: number;
  title: string;
  tasks: RoadmapTask[];
  cartIds: string[];
  selectedTaskId?: string | null;
  onCartClick: (id: string, shiftKey: boolean) => void;
  onStatusChange: (id: string, newStatus: TaskStatus) => void;
  onSelectTask?: (id: string) => void;
  toggleTierCart: (tierTasks: RoadmapTask[]) => void;
}

export const TierSection: React.FC<TierSectionProps> = React.memo(({
  tier,
  title,
  tasks,
  cartIds,
  selectedTaskId,
  onCartClick,
  onStatusChange,
  onSelectTask,
  toggleTierCart,
}) => {
  if (tasks.length === 0) return null;

  const allInTierInCart = tasks.length > 0 && tasks.every((t) => cartIds.includes(t.id));

  return (
    <Box key={tier} sx={{ mb: 5 }}>
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          mb: 2,
          pb: 1,
          borderBottom: '1px solid #49454f',
        }}
      >
        <Typography variant="h6" sx={{ color: 'primary.light' }}>
          {title}
        </Typography>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Button
            size="small"
            variant={allInTierInCart ? 'contained' : 'outlined'}
            color={allInTierInCart ? 'secondary' : 'primary'}
            startIcon={allInTierInCart ? <RemoveCartIcon /> : <AddCartIcon />}
            onClick={() => toggleTierCart(tasks)}
            sx={{
              fontSize: '0.78rem',
              py: 0.3,
              px: 1.5,
              borderRadius: 2,
              textTransform: 'none',
            }}
          >
            {allInTierInCart ? 'Tier一括解除' : 'Tier一括追加'}
          </Button>

          <Chip
            icon={undefined}
            label={`${tasks.length} 件`}
            size="small"
            sx={{ 
              bgcolor: '#49454f', 
              color: '#e6e0e9', 
              fontWeight: 600,
              transition: 'all 0.3s ease',
              width: 'auto',
              justifyContent: 'center',
            }}
          />
        </Box>
      </Box>

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))',
          gap: 2,
        }}
      >
        {tasks.map((t) => (
          <Box key={t.id}>
            <TaskCard
              task={t}
              inCart={cartIds.includes(t.id)}
              isSelected={selectedTaskId === t.id}
              onCartClick={onCartClick}
              onStatusChange={onStatusChange}
              onSelectTask={onSelectTask}
            />
          </Box>
        ))}
      </Box>
    </Box>
  );
});
