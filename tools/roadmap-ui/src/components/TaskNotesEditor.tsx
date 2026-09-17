import React from 'react';
import { Box, Typography, TextField } from '@mui/material';
import { EditNote as NoteIcon } from '@mui/icons-material';

interface TaskNotesEditorProps {
  taskId: string;
  notes: string;
  onNotesChange: (id: string, notes: string) => void;
}

export const TaskNotesEditor: React.FC<TaskNotesEditorProps> = ({ taskId, notes, onNotesChange }) => {
  return (
    <Box>
      <Typography
        variant="caption"
        sx={{ color: '#d0bcff', fontWeight: 700, mb: 0.6, display: 'flex', alignItems: 'center', gap: 0.6, fontSize: '0.75rem' }}
      >
        <NoteIcon sx={{ fontSize: 16 }} /> タスク詳細メモ (自動保存)
      </Typography>
      <TextField
        multiline
        rows={3}
        fullWidth
        variant="outlined"
        placeholder="メモ・補足を自由に入力..."
        value={notes || ''}
        onChange={(e) => onNotesChange(taskId, e.target.value)}
        sx={{
          bgcolor: 'rgba(0, 0, 0, 0.25)',
          borderRadius: 2,
          '& .MuiOutlinedInput-root': {
            fontSize: '0.84rem',
            color: '#e6e0e9',
            p: 1.5,
            '& fieldset': { borderColor: 'rgba(255, 255, 255, 0.08)' },
            '&:hover fieldset': { borderColor: 'rgba(208, 188, 255, 0.3)' },
            '&.Mui-focused fieldset': { borderColor: 'primary.main' },
          },
        }}
      />
    </Box>
  );
};
