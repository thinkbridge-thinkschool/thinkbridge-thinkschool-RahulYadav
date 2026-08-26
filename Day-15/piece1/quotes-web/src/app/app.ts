import { Component, inject, signal } from '@angular/core';
import { QuoteList } from './quote-list/quote-list';
import { QuoteDetail } from './quote-detail/quote-detail';
import { QuoteCreate } from './quote-create/quote-create';
import { Login } from './login/login';
import { AuthService } from './core/auth.service';
import type { Quote } from './core/quote.model';

@Component({
  selector: 'app-root',
  imports: [QuoteList, QuoteDetail, QuoteCreate, Login],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly auth = inject(AuthService);

  protected readonly title = signal('QuotesApi frontend');

  // Owned by the root component: passed down to QuoteList for highlighting
  // and to QuoteDetail to drive the detail fetch.
  protected readonly selectedId = signal<number | null>(null);

  // A quote created via QuoteCreate is selected immediately so it shows in
  // the detail pane, same as picking it from QuoteList.
  protected onQuoteCreated(quote: Quote): void {
    this.selectedId.set(quote.id);
  }
}
