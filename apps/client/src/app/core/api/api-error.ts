import { HttpErrorResponse } from '@angular/common/http';

/**
 * A structured, safe representation of an API failure. Every `ApiClient`
 * call rejects with this type instead of a raw `HttpErrorResponse`, so
 * features never need to branch on `HttpErrorResponse` themselves.
 *
 * `detail` (when present) is text the backend already built to be shown to
 * the user (its `ProblemDetails.detail`) — never a raw exception message,
 * stack trace or internal object.
 */
export interface ApiError {
  /** HTTP status code, or 0 when the request never reached the server. */
  status: number;
  title: string;
  detail?: string;
  type?: string;
  instance?: string;
}

interface ProblemDetailsBody {
  status?: number;
  title?: string;
  detail?: string;
  type?: string;
  instance?: string;
}

function isProblemDetailsBody(value: unknown): value is ProblemDetailsBody {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'status' in value || 'detail' in value)
  );
}

function defaultTitleFor(status: number): string {
  switch (status) {
    case 400:
      return 'This request is invalid.';
    case 401:
      return 'You need to sign in to continue.';
    case 403:
      return 'You do not have access to this resource.';
    case 404:
      return 'The requested resource was not found.';
    case 409:
      return 'This action conflicts with the current state.';
    case 422:
      return 'This request could not be processed.';
    default:
      return 'Something went wrong. Please try again.';
  }
}

/**
 * Maps any error thrown by an `HttpClient` request to a safe {@link ApiError}.
 * Never propagates a raw error object, a stack trace or internal server
 * details — only the backend's own `ProblemDetails` fields, when present.
 */
export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return {
      status: 0,
      title: 'Something went wrong. Please try again.'
    };
  }

  // status 0: the request never reached the server (offline, CORS, DNS,
  // connection refused, etc.) — never the browser's own error object.
  if (error.status === 0) {
    return {
      status: 0,
      title: 'Could not reach the server. Check your connection and try again.'
    };
  }

  if (isProblemDetailsBody(error.error)) {
    const body = error.error;

    return {
      status: body.status ?? error.status,
      title: body.title ?? defaultTitleFor(error.status),
      detail: body.detail,
      type: body.type,
      instance: body.instance
    };
  }

  // Anything else (an unexpected 500 with an HTML body, a malformed
  // response, ...) becomes a safe, generic fallback.
  return {
    status: error.status,
    title: defaultTitleFor(error.status)
  };
}
