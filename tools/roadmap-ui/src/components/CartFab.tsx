import React from 'react';
import { Box, Fab, Badge, Tooltip } from '@mui/material';
import { ContentCopy as CopyIcon } from '@mui/icons-material';
import { JulesIcon } from './JulesIcon';

interface CartFabProps {
  count: number;
  onCopyJulesPrompt: () => void;
  onCopyIdsOnly: () => void;
}

export const CartFab: React.FC<CartFabProps> = ({ count, onCopyJulesPrompt, onCopyIdsOnly }) => {
  if (count === 0) return null;

  return (
    <Box
      sx={{
        position: 'fixed',
        bottom: 32,
        right: 32,
        display: 'flex',
        flexDirection: 'column',
        gap: 1.5,
        zIndex: 1000,
      }}
    >
      <Tooltip title="タスクIDのみコピー" placement="left">
        <Fab
          color="secondary"
          aria-label="Copy Task IDs"
          onClick={onCopyIdsOnly}
          size="small"
          sx={{
            borderRadius: 2,
            alignSelf: 'center',
            bgcolor: '#4a4458',
            color: '#e8def8',
            '&:hover': { bgcolor: '#332d41' },
          }}
        >
          <CopyIcon fontSize="small" />
        </Fab>
      </Tooltip>

      <Tooltip title="Julesプロンプト生成" placement="left">
        <Fab
          color="primary"
          aria-label="Generate Jules Prompt"
          onClick={onCopyJulesPrompt}
          sx={{ borderRadius: 2 }}
        >
          <Badge badgeContent={count} color="error">
            <JulesIcon />
          </Badge>
        </Fab>
      </Tooltip>
    </Box>
  );
};
