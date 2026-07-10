import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentUploadStore } from '../../core/document-upload.store';
import { DocumentUpload } from './document-upload';

describe('DocumentUpload', () => {
  let fixture: ComponentFixture<DocumentUpload>;
  let store: DocumentUploadStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    fixture = TestBed.createComponent(DocumentUpload);
    store = TestBed.inject(DocumentUploadStore);
  });

  it('should hand a selected file to the store', () => {
    const spy = spyOn(store, 'upload');
    const file = new File([new Uint8Array([1])], 'a.pdf');
    const input = { files: [file], value: 'x' } as unknown as HTMLInputElement;

    fixture.componentInstance.onFileSelected(input);

    expect(spy).toHaveBeenCalledWith(file, null);
  });

  it('should mark the zone active on drag over and inactive on leave', () => {
    fixture.componentInstance.onDragOver(new DragEvent('dragover'));
    expect(fixture.componentInstance.dragging()).toBeTrue();

    fixture.componentInstance.onDragLeave();
    expect(fixture.componentInstance.dragging()).toBeFalse();
  });

  it('should render the store error', () => {
    store.error.set('Nieobsługiwany typ pliku.');
    fixture.detectChanges();

    const error = (fixture.nativeElement as HTMLElement).querySelector('.upload__error');
    expect(error?.textContent).toContain('Nieobsługiwany typ');
  });
});
