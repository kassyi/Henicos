import React from 'react';
import { Accordion, AccordionSummary, AccordionDetails, Box, Typography, Chip } from '@mui/material';
import { ExpandMore as ExpandMoreIcon, HelpOutlined as HelpIcon } from '@mui/icons-material';

export const InfoAccordion: React.FC = () => {
  return (
    <Accordion sx={{ mb: 4, bgcolor: '#1d1b20', border: '1px solid #49454f' }}>
      <AccordionSummary
        expandIcon={<ExpandMoreIcon />}
        sx={{ minHeight: 48, '& .MuiAccordionSummary-content': { my: 1.5 } }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <HelpIcon fontSize="small" sx={{ color: 'primary.light' }} />
          <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
            スコアと用語の解説（クリックで展開）
          </Typography>
        </Box>
      </AccordionSummary>
      <AccordionDetails sx={{ borderTop: '1px solid #49454f', bgcolor: '#141218', p: 3 }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr 1fr' }, gap: 4 }}>
          {/* 因子 */}
          <Box>
            <Typography
              variant="caption"
              sx={{ fontWeight: 700, color: 'primary.light', display: 'block', mb: 1 }}
            >
              【因子 (Factors)】
            </Typography>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, fontSize: '0.8rem' }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip
                  label="依存度 (B)"
                  size="small"
                  sx={{
                    minWidth: 80,
                    fontWeight: 700,
                    bgcolor: '#332d41',
                    color: '#e8def8',
                    fontSize: '0.72rem',
                  }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  <strong>1</strong> (自己完結) &rarr; <strong>3</strong> (一部依存) &rarr;{' '}
                  <strong>5</strong> (完全ブロック/最優先)
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip
                  label="難易度 (D)"
                  size="small"
                  sx={{
                    minWidth: 80,
                    fontWeight: 700,
                    bgcolor: '#332d41',
                    color: '#e8def8',
                    fontSize: '0.72rem',
                  }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  <strong>1</strong> (極めて簡単) &rarr; <strong>3</strong> (普通) &rarr;{' '}
                  <strong>5</strong> (最難関/大設計)
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip
                  label="汎用性 (G)"
                  size="small"
                  sx={{
                    minWidth: 80,
                    fontWeight: 700,
                    bgcolor: '#332d41',
                    color: '#e8def8',
                    fontSize: '0.72rem',
                  }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  <strong>1</strong> (ニッチ/稀) &rarr; <strong>3</strong> (通常) &rarr;{' '}
                  <strong>5</strong> (常時使われる)
                </Typography>
              </Box>
            </Box>
          </Box>

          {/* ステータス */}
          <Box>
            <Typography
              variant="caption"
              sx={{ fontWeight: 700, color: 'primary.light', display: 'block', mb: 1 }}
            >
              【ステータス】
            </Typography>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.8, fontSize: '0.8rem' }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip
                  label="未完"
                  size="small"
                  sx={{
                    minWidth: 70,
                    fontWeight: 600,
                    bgcolor: '#141218',
                    color: 'text.secondary',
                    border: '1px solid #49454f',
                    fontSize: '0.7rem',
                  }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  未着手
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip
                  label="🛠️ 実装"
                  size="small"
                  sx={{
                    minWidth: 70,
                    fontWeight: 700,
                    bgcolor: '#2e5b44',
                    color: '#c4eed0',
                    fontSize: '0.7rem',
                  }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  実装・テスト完了
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip
                  label="⚡ パフォ"
                  size="small"
                  sx={{
                    minWidth: 70,
                    fontWeight: 700,
                    bgcolor: '#5c256b',
                    color: '#f3e5f5',
                    fontSize: '0.7rem',
                  }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  GPU最適化・チューニング完了
                </Typography>
              </Box>
            </Box>
          </Box>

          {/* ドメイン */}
          <Box>
            <Typography
              variant="caption"
              sx={{ fontWeight: 700, color: 'primary.light', display: 'block', mb: 1 }}
            >
              【ドメイン】
            </Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1, fontSize: '0.8rem' }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.8 }}>
                <Chip
                  label="SYN"
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 700, fontSize: '0.72rem', borderColor: '#49454f' }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  構文 (Syntax)
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.8 }}>
                <Chip
                  label="GBI"
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 700, fontSize: '0.72rem', borderColor: '#49454f' }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  GpuBigInteger
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.8 }}>
                <Chip
                  label="GSH"
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 700, fontSize: '0.72rem', borderColor: '#49454f' }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  トランスパイラ (Core)
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.8 }}>
                <Chip
                  label="GEN"
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 700, fontSize: '0.72rem', borderColor: '#49454f' }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  ジェネレータ基盤
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.8 }}>
                <Chip
                  label="GBF"
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 700, fontSize: '0.72rem', borderColor: '#49454f' }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  GpuBigFloat
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.8 }}>
                <Chip
                  label="BCL"
                  size="small"
                  variant="outlined"
                  sx={{ fontWeight: 700, fontSize: '0.72rem', borderColor: '#49454f' }}
                />
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  基本クラスライブラリ
                </Typography>
              </Box>
            </Box>
          </Box>
        </Box>
      </AccordionDetails>
    </Accordion>
  );
};
