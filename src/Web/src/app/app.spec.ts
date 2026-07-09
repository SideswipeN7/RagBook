import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('should not request the quota until the session has been established', () => {
    // Arrange
    const fixture = TestBed.createComponent(App);

    // Act — first change detection triggers ngOnInit
    fixture.detectChanges();
    const session = controller.expectOne('/api/session');

    // Assert — no cookie-less quota request races the session request
    expect(() => controller.expectNone('/api/quota')).not.toThrow();
    session.flush({ isNew: true, resourceCount: 0 });
    expect(() =>
      controller.expectOne('/api/quota').flush({
        usedDocuments: 0,
        maxDocuments: 10,
        usedMb: 0,
        maxTotalMb: 50,
        maxFileSizeMb: 10,
        canUpload: true,
      }),
    ).not.toThrow();
  });
});
