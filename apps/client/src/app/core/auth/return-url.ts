export const fallbackAuthenticatedUrl = '/';

export function sanitizeReturnUrl(value: string | null | undefined): string | null {
  if (!value) {
    return null;
  }

  if (!value.startsWith('/') || value.startsWith('//') || value.includes('://')) {
    return null;
  }

  if (value === '/login' || value.startsWith('/login?') || value.startsWith('/login#')) {
    return null;
  }

  return value;
}

