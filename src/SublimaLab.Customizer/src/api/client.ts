import type { DesignDocument } from '../design/model';
import type { CartResponse, DesignDto, ProductDetail, UploadDto, ValidationResult } from './types';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly errors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

/**
 * Same-origin API client. The HttpOnly auth cookie is sent by the browser; unsafe requests carry
 * the antiforgery token in X-XSRF-TOKEN, refreshed once if the server reports it as invalid.
 */
export class ApiClient {
  private token: string | null = null;

  async getProduct(slug: string) {
    return this.request<ProductDetail>('GET', `/api/products/${encodeURIComponent(slug)}`);
  }

  async getCart() {
    return this.request<CartResponse>('GET', '/api/cart');
  }

  async addToCart(body: { productId: string; variantId: string; quantity: number; design: DesignDocument; previewUploadId: string | null }) {
    return this.request<CartResponse>('POST', '/api/cart/items', body);
  }

  async updateCartItem(id: string, body: { quantity: number; variantId: string; design: DesignDocument; previewUploadId: string | null }) {
    return this.request<CartResponse>('PUT', `/api/cart/items/${id}`, body);
  }

  async validateDesign(productId: string, design: DesignDocument) {
    return this.request<ValidationResult>('POST', '/api/designs/validate', { productId, design });
  }

  async getDesign(id: string) {
    return this.request<DesignDto>('GET', `/api/designs/${id}`);
  }

  async saveDesign(body: { productId: string; name: string; design: DesignDocument; previewUploadId: string | null }, id?: string | null) {
    return id ? this.request<DesignDto>('PUT', `/api/designs/${id}`, body) : this.request<DesignDto>('POST', '/api/designs', body);
  }

  async upload(file: Blob, fileName: string, kind: 'Artwork' | 'Preview' = 'Artwork') {
    const form = new FormData();
    form.append('file', file, fileName);
    form.append('kind', kind);
    return this.request<UploadDto>('POST', '/api/uploads', form);
  }

  async getUpload(id: string) {
    return this.request<UploadDto>('GET', `/api/uploads/${id}`);
  }

  private async request<T>(method: string, url: string, body?: unknown, retried = false): Promise<T> {
    const unsafe = method !== 'GET' && method !== 'HEAD';
    if (unsafe && !this.token) await this.refreshToken();

    const headers: Record<string, string> = { Accept: 'application/json' };
    if (unsafe && this.token) headers['X-XSRF-TOKEN'] = this.token;
    let payload: BodyInit | undefined;
    if (body instanceof FormData) {
      payload = body;
    } else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }

    let response: Response;
    try {
      response = await fetch(url, { method, headers, body: payload, credentials: 'same-origin' });
    } catch {
      throw new ApiError(0, 'NETWORK_ERROR', "We couldn't reach SublimaLab. Check your connection and try again.");
    }

    if (response.ok) {
      return (response.status === 204 ? undefined : await response.json()) as T;
    }

    const error = await readError(response);
    if (error.code === 'CSRF_TOKEN_INVALID' && !retried) {
      await this.refreshToken();
      return this.request<T>(method, url, body, true);
    }
    throw error;
  }

  private async refreshToken() {
    const response = await fetch('/api/antiforgery/token', { credentials: 'same-origin' });
    this.token = response.ok ? ((await response.json()) as { token: string }).token : null;
  }
}

async function readError(response: Response): Promise<ApiError> {
  try {
    const body = (await response.json()) as { code?: string; message?: string; errors?: Record<string, string[]> };
    if (body.code) return new ApiError(response.status, body.code, body.message ?? 'Something went wrong.', body.errors ?? {});
  } catch {
    // Not JSON.
  }
  return new ApiError(
    response.status,
    'HTTP_ERROR',
    response.status === 413 ? 'That file is too large.' : response.status === 429 ? 'Too many requests. Please wait a moment.' : 'Something went wrong. Please try again.',
  );
}

/** First field error, or the general message. */
export const errorMessage = (error: unknown): string => {
  if (error instanceof ApiError) {
    const field = Object.values(error.errors).flat()[0];
    return field ?? error.message;
  }
  return 'Something went wrong. Please try again.';
};
