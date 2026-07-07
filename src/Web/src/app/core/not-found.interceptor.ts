import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotFoundNotifier } from './not-found-notifier';

/**
 * Maps every backend 404 to a "resource does not exist" experience (US-01 AC-3/§IX). Because the API
 * returns 404 (never 403) for another session's resource, the UI never distinguishes "not yours" from
 * "does not exist".
 */
export const notFoundInterceptor: HttpInterceptorFn = (request, next) => {
  const notifier = inject(NotFoundNotifier);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        notifier.notify('This resource does not exist.');
      }

      return throwError(() => error);
    }),
  );
};
