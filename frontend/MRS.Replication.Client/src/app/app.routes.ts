import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'setup', pathMatch: 'full' },
  {
    path: 'setup',
    loadComponent: () => import('./features/setup-wizard/setup-wizard').then((m) => m.SetupWizard)
  },
  {
    path: 'dashboard',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard)
  },
  { path: '**', redirectTo: 'setup' }
];
