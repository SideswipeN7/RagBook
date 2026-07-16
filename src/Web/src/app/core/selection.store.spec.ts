import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SelectionStore } from './selection.store';
import { TreeStore } from './tree.store';
import { QuotaStore } from './quota.store';

describe('SelectionStore', () => {
  let store: SelectionStore;
  let controller: HttpTestingController;
  let tree: TreeStore;
  let quota: QuotaStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    store = TestBed.inject(SelectionStore);
    controller = TestBed.inject(HttpTestingController);
    tree = TestBed.inject(TreeStore);
    quota = TestBed.inject(QuotaStore);
    spyOn(tree, 'refresh');
    spyOn(quota, 'refresh');
  });

  afterEach(() => controller.verify());

  it('toggles ids and reports count / selectedIds', () => {
    store.toggle('a');
    store.toggle('b');

    expect(store.count()).toBe(2);
    expect(store.has('a')).toBeTrue();
    expect(store.selectedIds()).toEqual(['a', 'b']);

    store.toggle('a'); // untick

    expect(store.count()).toBe(1);
    expect(store.has('a')).toBeFalse();
  });

  it('clears the whole selection', () => {
    store.toggle('a');
    store.toggle('b');

    store.clear();

    expect(store.count()).toBe(0);
    expect(store.hasSelection()).toBeFalse();
  });

  it('selects a contiguous range within a folder (Shift-click)', () => {
    store.selectRange(['a', 'b', 'c', 'd'], 'b', 'd');

    expect(store.selectedIds()).toEqual(['b', 'c', 'd']);
    expect(store.has('a')).toBeFalse();
  });

  it('posts a bulk move, then clears the selection and refreshes tree + quota', () => {
    store.toggle('a');
    store.toggle('b');

    store.bulkMove('folder-1');

    const request = controller.expectOne('/api/documents/bulk-move');
    expect(request.request.body).toEqual({ ids: ['a', 'b'], targetFolderId: 'folder-1' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(store.count()).toBe(0);
    expect(tree.refresh).toHaveBeenCalled();
    expect(quota.refresh).toHaveBeenCalled();
  });

  it('posts a bulk delete with the selected ids', () => {
    store.toggle('a');

    store.bulkDelete();

    const request = controller.expectOne('/api/documents/bulk-delete');
    expect(request.request.body).toEqual({ ids: ['a'] });
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(tree.refresh).toHaveBeenCalled();
  });

  it('marks the offending ids on a 422 and keeps the selection', () => {
    store.toggle('a');
    store.toggle('bad');

    store.bulkDelete();

    controller.expectOne('/api/documents/bulk-delete').flush(
      { code: 'document.bulk_validation_failed', failures: [{ id: 'bad', code: 'document.read_only' }] },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(store.hasFailed('bad')).toBeTrue();
    expect(store.hasFailed('a')).toBeFalse();
    expect(store.count()).toBe(2); // selection NOT cleared, so the user can fix it
    expect(store.bulkError()).not.toBeNull();
    expect(tree.refresh).not.toHaveBeenCalled();
  });

  it('un-marks a failed id when it is toggled', () => {
    store.toggle('bad');
    store.bulkDelete();
    controller.expectOne('/api/documents/bulk-delete').flush(
      { code: 'document.bulk_validation_failed', failures: [{ id: 'bad', code: 'document.not_found' }] },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    expect(store.hasFailed('bad')).toBeTrue();

    store.toggle('bad'); // untick the offending row

    expect(store.hasFailed('bad')).toBeFalse();
  });
});
