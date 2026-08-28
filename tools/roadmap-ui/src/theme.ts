import { createTheme } from '@mui/material/styles';

// Material Design 3 (M3) Dark Palette Tokens
export const m3DarkTheme = createTheme({
  palette: {
    mode: 'dark',
    primary: {
      main: '#d0bcff', // M3 Dark Primary
      contrastText: '#381e72',
      light: '#eaddff',
      dark: '#4f378b',
    },
    secondary: {
      main: '#ccc2dc',
      contrastText: '#332d41',
      light: '#e8def8',
      dark: '#4a4458',
    },
    error: {
      main: '#f2b8b5',
      contrastText: '#601410',
      light: '#f9dedc',
      dark: '#8c1d18',
    },
    background: {
      default: '#141218', // M3 Surface
      paper: '#211f26',   // M3 Surface Container
    },
    text: {
      primary: '#e6e0e9',   // onSurface
      secondary: '#cac4d0', // onSurfaceVariant
    },
    divider: '#49454f',     // outlineVariant
  },
  typography: {
    fontFamily: '"Roboto", "Inter", "Segoe UI", sans-serif',
    h4: {
      fontWeight: 600,
      letterSpacing: '-0.02em',
    },
    h6: {
      fontWeight: 600,
      letterSpacing: '0.01em',
    },
    subtitle1: {
      fontSize: '0.95rem',
      fontWeight: 500,
    },
    body2: {
      color: '#cac4d0',
    },
  },
  shape: {
    borderRadius: 8, // Sleek, modern subtle radius
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: {
          backgroundColor: '#1d1b20',
          borderRadius: 10,
          border: '1px solid #49454f',
          boxShadow: 'none',
          transition: 'all 0.2s ease-in-out',
          '&:hover': {
            backgroundColor: '#2b2930',
            borderColor: '#938f99',
            transform: 'translateY(-2px)',
          },
        },
      },
    },
    MuiButton: {
      styleOverrides: {
        root: {
          borderRadius: 8, // Modern subtle rounded corners
          textTransform: 'none',
          fontWeight: 600,
          padding: '8px 20px',
        },
        contained: {
          backgroundColor: '#d0bcff',
          color: '#381e72',
          '&:hover': {
            backgroundColor: '#eaddff',
          },
        },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: {
          borderRadius: 6,
          fontWeight: 500,
        },
        outlined: {
          borderColor: '#49454f',
        },
      },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: 8,
          '& .MuiOutlinedInput-notchedOutline': {
            borderColor: '#49454f',
          },
          '&:hover .MuiOutlinedInput-notchedOutline': {
            borderColor: '#938f99',
          },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': {
            borderColor: '#d0bcff',
          },
        },
      },
    },
  },
});
