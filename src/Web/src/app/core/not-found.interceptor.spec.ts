import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NotFoundNotifier } from './not-found-notifier';
import { notFoundInterceptor } from './not-found.interceptor';

describe('notFoundInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let notifier: NotFoundNotifier;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([notFoundInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    notifier = TestBed.inject(NotFoundNotifier);
  });

  afterEach(() => controller.verify());

  it('should surface a "does not exist" message when the backend returns 404', () => {
    http.get('/api/resources/does-not-exist').subscribe({
      error: (error: HttpErrorResponse) => expect(error.status).toBe(404),
    });

    controller.expectOne('/api/resources/does-not-exist').flush(
      { code: 'session.resource_not_found' },
      { status: 404, statusText: 'Not Found' },
    );

    expect(notifier.message()).toBe('This resource does not exist.');
  });

  it('should not surface a message for successful responses', () => {
    http.get('/api/session').subscribe();

    controller.expectOne('/api/session').flush({ isNew: true, resourceCount: 0 });

    expect(notifier.message()).toBeNull();
  });
});
