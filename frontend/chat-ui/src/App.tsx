import { ThemeProvider, createTheme, CssBaseline } from '@mui/material'
import { ChatWindow } from './components/ChatWindow'

const theme = createTheme({
  palette: {
    background: {
      default: '#ffffff',
    },
  },
  typography: {
    fontFamily: 'Roboto, sans-serif',
  },
})

function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <ChatWindow />
    </ThemeProvider>
  )
}

export default App
