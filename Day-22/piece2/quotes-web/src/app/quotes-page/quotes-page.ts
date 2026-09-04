import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { QuoteList } from '../quote-list/quote-list';
import { AuthService } from '../core/auth.service';

// Hosts the existing QuoteList alongside a nested router-outlet that renders
// either the quote detail (/quotes/:id) or the create form (/quotes/new) —
// the same master-detail layout the app had before routing was introduced,
// now backed by real routes instead of a parent-owned signal.
@Component({
  selector: 'app-quotes-page',
  imports: [QuoteList, RouterLink, RouterOutlet],
  templateUrl: './quotes-page.html',
  styleUrl: './quotes-page.css',
})
export class QuotesPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly auth = inject(AuthService);

  // Whether the nested outlet currently has an activated component, so the
  // aside can fall back to the "select a quote" placeholder when it doesn't
  // (mirrors QuoteDetail's old selectedId===null empty state).
  protected readonly detailActive = signal(false);

  // The active child route's :id param (if any), purely to highlight the
  // matching row in QuoteList — re-derived on every completed navigation.
  protected readonly selectedId = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.readActiveDetailId()),
    ),
    { initialValue: this.readActiveDetailId() },
  );

  onSelect(id: number): void {
    this.router.navigate(['/quotes', id]);
  }

  private readActiveDetailId(): number | null {
    const idParam = this.route.snapshot.firstChild?.paramMap.get('id');
    if (!idParam) {
      return null;
    }
    const id = Number(idParam);
    return Number.isInteger(id) ? id : null;
  }
}
