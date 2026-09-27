import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url.token';

@Injectable({
  providedIn: 'root'
})
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL).replace(/\/$/, '');

  get<T>(path: string): Observable<T> {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    return this.http.get<T>(`${this.baseUrl}${normalizedPath}`);
  }
}
