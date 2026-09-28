import { HttpParams } from '@angular/common/http';

/**
 * Query parameters for an `ApiClient` call. `undefined`/`null` values are
 * omitted (so callers can pass an optional filter straight through without
 * an `if`), and an array value is repeated once per item — the shape the
 * backend's admin/availability filters already expect.
 */
export type ApiQueryParams = Record<
  string,
  string | number | boolean | readonly (string | number)[] | undefined | null
>;

export function toHttpParams(params?: ApiQueryParams): HttpParams {
  let httpParams = new HttpParams();

  if (!params) {
    return httpParams;
  }

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null) {
      continue;
    }

    if (Array.isArray(value)) {
      for (const item of value) {
        httpParams = httpParams.append(key, String(item));
      }
    } else {
      httpParams = httpParams.set(key, String(value));
    }
  }

  return httpParams;
}
