export interface TokenDto {
  accessToken: string
  expiresAt: string
  userId: string
}

const TOKEN_KEY = 'auth_token'
const TOKEN_EXPIRY_KEY = 'auth_token_expiry'

class AuthService {
  async login(email: string, password: string): Promise<TokenDto> {
    const response = await fetch('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    })

    if (!response.ok) {
      throw new Error(`${response.status}`)
    }

    const dto: TokenDto = await response.json()
    this._store(dto)
    return dto
  }

  async register(email: string, password: string, displayName: string): Promise<TokenDto> {
    const response = await fetch('/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, displayName }),
    })

    if (!response.ok) {
      throw new Error(`${response.status}`)
    }

    const dto: TokenDto = await response.json()
    this._store(dto)
    return dto
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(TOKEN_EXPIRY_KEY)
  }

  getToken(): string | null {
    if (this._isExpired()) {
      this.logout()
      return null
    }
    return localStorage.getItem(TOKEN_KEY)
  }

  isAuthenticated(): boolean {
    return !!this.getToken()
  }

  private _store(dto: TokenDto): void {
    localStorage.setItem(TOKEN_KEY, dto.accessToken)
    localStorage.setItem(TOKEN_EXPIRY_KEY, dto.expiresAt)
  }

  private _isExpired(): boolean {
    const expiry = localStorage.getItem(TOKEN_EXPIRY_KEY)
    if (!expiry) return false
    return new Date(expiry) <= new Date()
  }
}

export const authService = new AuthService()
