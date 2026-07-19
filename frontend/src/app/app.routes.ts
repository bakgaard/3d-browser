import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/search/search.page').then((m) => m.SearchPage),
    title: 'Patreon 3D Browser',
  },
  {
    path: 'objects/:id',
    loadComponent: () =>
      import('./pages/object-detail/object-detail.page').then(
        (m) => m.ObjectDetailPage,
      ),
    title: 'Object · Patreon 3D Browser',
  },
  { path: '**', redirectTo: '' },
];
