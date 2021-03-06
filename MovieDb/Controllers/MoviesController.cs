﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Web.Mvc;
using MovieDb.Models;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Identity;
using MovieDb.ViewModels;

namespace MovieDb.Controllers
{
    public class MoviesController : Controller
    {

        protected ApplicationDbContext db = new ApplicationDbContext();
        protected UserManager<ApplicationUser> um;

        // GET: Movies
        public async Task<ActionResult> Index()
        {

            var movies = db.Movies.Include(m => m.Genre);

            return View(await movies.ToListAsync());

        }

        public ActionResult Search(String searchText)
        {
            List<Movie> Movies = new List<Movie>();

            if (!String.IsNullOrEmpty(searchText))
            {
                Movies = db.Movies.Include(m => m.Genre)
                     .Where(s => s.Title.Contains(searchText)
                     || s.GenreName == searchText
                     || s.Appearances.Any(a => a.Actor.Name == searchText)
                     ).ToList();
            }

            Session.Add("SearchType", "Default");
            Session.Add("SearchedFor", searchText);

            return View("SearchResult", Movies);
        }


        public ActionResult AdvancedSearch()
        {
            List<Movie> Movies = new List<Movie>();

            List<Genre> Genres = new List<Genre>();
            Genre a = new Genre();
            Genres.Add(a);
            ViewBag.Genres = new SelectList(Genres.Concat(db.Genres.ToList()), "Name", "Name");

            List<DateTime?> Dates = new List<DateTime?>();
            Dates.Add(null);
            for (int i = 2017; i > 1900; i--)
            {
                Dates.Add(new DateTime(i, 12, 31));
            }

            ViewBag.DatesFrom = new SelectList(Dates);
            ViewBag.DatesTo = new SelectList(Dates);

            List<double> Rates = new List<double>();
            for (double i = 1; i <= 5; i += 1)
            {
                Rates.Add(i);
            }
            ViewBag.Rates = new SelectList(Rates);

            List<Actor> Actors = new List<Actor>();
            Actor b = new Actor();
            Actors.Add(b);
            ViewBag.Actors = new SelectList(Actors.Concat(db.Actors.ToList()), "Name", "Name");

            return View();
        }

        [HttpPost]
        public ActionResult AdvancedSearch(SearchViewModel model)
        {
            DateTime DateFrom, DateTo;
            String ActorName = (model.ActorName != null) ? model.ActorName : "";
            String GenreName = (model.GenreName != null) ? model.GenreName : "";

            if (model.DateFrom.HasValue)
                DateFrom = model.DateFrom.Value;
            else
                DateFrom = DateTime.MinValue;
            if (model.DateTo.HasValue)
                DateTo = model.DateTo.Value;
            else
                DateTo = DateTime.MaxValue;


            var Movies = db.Movies.Include(m => m.Genre)
                .Where(n => n.GenreName.Contains(GenreName) &&
                DbFunctions.TruncateTime(n.ReleaseDate) >= DateFrom &&
                DbFunctions.TruncateTime(n.ReleaseDate) <= DateTo &&
                n.Appearances.Any(c => c.Actor.Name.Contains(ActorName)) 
                ).ToList();

            if (model.Rate > 1)
            {
                List <Movie> MoviesByAverageRating = new List<Movie>();
                var Collection = db.Movies.Include(m => m.Genre).ToList();
                foreach (Movie movie in Collection.Where(i => MovieAverageRating(i) >= model.Rate))
                {
                    MoviesByAverageRating.Add(movie);
                }

                if (Movies.Count() != 0)
                    Movies = Movies.Intersect(MoviesByAverageRating).ToList();
                else if (model.GenreName != null && DateFrom != DateTime.MinValue && DateTo != DateTime.MaxValue && model.ActorName != null)
                    Movies = MoviesByAverageRating;
            }

            Session.Add("SearchType", "Advanced");

            return View("SearchResult", Movies);
        }

        // GET: Movies/Details/5
        public async Task<ActionResult> Details(int? id)
        {
            Session["MovieId"] = id;

            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            Movie Movie = await db.Movies.FindAsync(id);
            var movie = db.Movies.Include(i => i.Users).Where(u => u.ID == id).First();

            //Getting the actors 
            var actors = await db.Appearances.Include(a => a.Actor).Where(a => a.MovieId == movie.ID).Select(a => a.Actor).ToListAsync();

            MovieActorViewModel movieActorViewModel = new MovieActorViewModel();
            movieActorViewModel.Movie = movie;
            movieActorViewModel.Actors = actors;
            movieActorViewModel.Comments = db.Comments.Include(u => u.User).Where(c => c.MovieID == id).ToList();

            ViewBag.Rating = getMovieRate(id).ToString("0.00");
            ViewBag.NumberOfRatings = getNumberOfRates(id);

            //Checking is the movie at the current user's favorites 
            //and getting the current user's movie rating
            if (HttpContext.User.Identity.IsAuthenticated)
            {
                var userid = User.Identity.GetUserId();
                um = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
                ApplicationUser appuser = await um.FindByIdAsync(userid);
                var user = db.Users.Include(i => i.Movies).Where(u => u.Id == userid).First();

                if (!movie.Users.Any(i => i.Id == user.Id) ||
                    !user.Movies.Any(u => u.ID == movie.ID))
                {
                    ViewBag.Favorited = false;
                }
                else
                {
                    ViewBag.Favorited = true;
                }

                int usersRating = getUsersRate(id, userid);
                ViewBag.UsersRating = usersRating;

            }
            if (movie == null)
            {
                return HttpNotFound();
            }

            return View(movieActorViewModel);
        }


        public double getMovieRate(int? movieid)
        {
            var ratings = db.Ratings.Where(m => m.MovieID == movieid).Select(r => r.Value).ToList();
            return getMovieRateLogic(ratings);
        }

        public double getMovieRateLogic(List<int> ratings)
        {
            double value = 0;
            int cnt = 0;
            foreach (int val in ratings)
            {
                cnt++;
                value += val;
            }
            if (cnt != 0)
                value /= cnt;

            return value;
        }


        public int getNumberOfRates(int? movieid)
        {
            int number = db.Ratings.Where(m => m.MovieID == movieid).Count();
            return number;
        }


        //Getting the User's rating for the movie
        public int getUsersRate(int? movieid, string userid)
        {
            int value;
            var movie = db.Movies.Include(r => r.Ratings).Where(u => u.ID == movieid).First();
            var user = db.Users.Include(r => r.Ratings).Where(u => u.Id == userid).First();

            if (movie.Ratings.Any(m => m.UserID == userid) || user.Ratings.Any(m => m.MovieID == movieid))
            {
                value = db.Ratings.Where(u => u.UserID == userid).Where(m => m.MovieID == movieid).Select(v => v.Value).First();
            }
            else
            {
                return 0;
            }

            return value;
        }


        public async Task<ActionResult> Rate(int value, int id)
        {
            if (User != null && User.Identity != null) {

                var userid = User.Identity.GetUserId();
                um = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
                ApplicationUser appuser = await um.FindByIdAsync(User.Identity.GetUserId());
                var movie = db.Movies.Include(r => r.Ratings).Where(u => u.ID == id).First();
                var user = db.Users.Include(r => r.Ratings).Where(u => u.Id == userid).First();

                Rating Rating = new Rating
                {
                    Value = value,
                    UserID = userid,
                    MovieID = id
                };

                if (!movie.Ratings.Any(m => m.UserID == userid) || !user.Ratings.Any(m => m.MovieID == id)) {
                    db.Ratings.Add(Rating);
                    await db.SaveChangesAsync();
                } else
                {
                    var ratingID = movie.Ratings.Where(m => m.UserID == userid).First().ID;
                    Rating r = db.Ratings.FirstOrDefault(x => x.ID == ratingID);
                    r.Value = value;
                }

            }
            await db.SaveChangesAsync();
            return RedirectToAction("Details", new { id = id });
        }


        //Drop movie from the current user's favorites list
        public async Task<ActionResult> ToFavorites(int id)
        {
            Movie Movie = await db.Movies.FindAsync(id);
            var userid = User.Identity.GetUserId();
            um = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            ApplicationUser appuser = await um.FindByIdAsync(User.Identity.GetUserId());

            var movie = db.Movies.Include(i => i.Users).Where(u => u.ID == id).First();
            var user = db.Users.Include(i => i.Movies).Where(u => u.Id == userid).First();

            if (!movie.Users.Any(i => i.Id == user.Id) ||
                !user.Movies.Any(u => u.ID == movie.ID))
            {
                Movie.Users.Add(user);
                appuser.Movies.Add(movie);
                await db.SaveChangesAsync();
            }

            return RedirectToAction("Details", new { id = id });
        }

        //Drop movie from the current user's favorites list
        public async Task<ActionResult> DropFromFavorites(int id)
        {
            Movie Movie = await db.Movies.FindAsync(id);
            var userid = User.Identity.GetUserId();
            um = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            ApplicationUser appuser = await um.FindByIdAsync(User.Identity.GetUserId());

            var movie = db.Movies.Include(i => i.Users).Where(u => u.ID == id).First();
            var user = db.Users.Include(i => i.Movies).Where(u => u.Id == userid).First();

            if (movie.Users.Any(i => i.Id == user.Id) ||
                user.Movies.Any(u => u.ID == movie.ID))
            {
                Movie.Users.Remove(user);
                appuser.Movies.Remove(movie);
                await db.SaveChangesAsync();
            }

            return RedirectToAction("Details", new { id = id });
        }

        //Drop movie from the current user's favorites list
        public async Task<ActionResult> DropFromFavoritesFromList(int id)
        {
            Movie Movie = await db.Movies.FindAsync(id);
            var userid = User.Identity.GetUserId();
            um = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            ApplicationUser appuser = await um.FindByIdAsync(User.Identity.GetUserId());

            var movie = db.Movies.Include(i => i.Users).Where(u => u.ID == id).First();
            var user = db.Users.Include(i => i.Movies).Where(u => u.Id == userid).First();

            if (movie.Users.Any(i => i.Id == user.Id) ||
                user.Movies.Any(u => u.ID == movie.ID))
            {
                Movie.Users.Remove(user);
                appuser.Movies.Remove(movie);
                await db.SaveChangesAsync();
            }

            return RedirectToAction("Favorites" , new { id = id });
        }


        public async Task<ActionResult> Favorites()
        {
            var userid = User.Identity.GetUserId();
            var movies = db.Movies.Include(m => m.Genre)
                .Where(x => x.Users.Any(c => c.Id == userid))
                ;

            return View(await movies.ToListAsync());
        }


        // GET: Movies/Create
        public ActionResult Create()
        {
            ViewBag.GenreName = new SelectList(db.Genres, "Name", "Name");
            return View();
        }

        // POST: Movies/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create([Bind(Include = "ID,Title,ReleaseDate,Description,CoverPicture,GenreName")] Movie movie)
        {
            if (ModelState.IsValid)
            {
                db.Movies.Add(movie);
                await db.SaveChangesAsync();
                return RedirectToAction("Index");
            }

            ViewBag.GenreName = new SelectList(db.Genres, "Name", "Name", movie.GenreName);
            return View(movie);
        }

        // GET: Movies/Edit/5
        public async Task<ActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Movie movie = await db.Movies.FindAsync(id);
            if (movie == null)
            {
                return HttpNotFound();
            }
            ViewBag.GenreName = new SelectList(db.Genres, "Name", "Name", movie.GenreName);
            return View(movie);
        }

        // POST: Movies/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit([Bind(Include = "ID,Title,ReleaseDate,Description,CoverPicture,GenreName")] Movie movie)
        {
            if (ModelState.IsValid)
            {
                db.Entry(movie).State = EntityState.Modified;
                await db.SaveChangesAsync();
                return RedirectToAction("Index");
            }
            ViewBag.GenreName = new SelectList(db.Genres, "Name", "Name", movie.GenreName);
            return View(movie);
        }

        // GET: Movies/Delete/5
        public async Task<ActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Movie movie = await db.Movies.FindAsync(id);
            if (movie == null)
            {
                return HttpNotFound();
            }
            return View(movie);
        }

        // POST: Movies/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirmed(int id)
        {
            Movie movie = await db.Movies.FindAsync(id);
            db.Movies.Remove(movie);
            await db.SaveChangesAsync();
            return RedirectToAction("Index");
        }


        public ActionResult SendComment()
        {

            return View();
        }


        [HttpPost]
        public async Task<ActionResult> SendComment(MovieActorViewModel Model)
        {
            int movieId = Model.Movie.ID;

            if (HttpContext.User.Identity.IsAuthenticated) {
                 var newComment = new Comment { Text = Model.CommentText, date = DateTime.Now, UserID = User.Identity.GetUserId(), MovieID = movieId };
                 db.Comments.Add(newComment);
                 await db.SaveChangesAsync();
          }

            return RedirectToAction("Details", new { id = movieId });
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        public List<KeyValuePair<Movie, double>> MoviesRanked()
        {
            List<KeyValuePair<Movie, double>> Dictionary = new List<KeyValuePair<Movie, double>>();

            var movies = db.Movies;
            foreach(Movie movie in movies) { 
                Dictionary.Add(new KeyValuePair<Movie,double>(movie, MovieAverageRating(movie)));
            }

            var Ranking = Dictionary.ToList();
            Ranking.Sort((p, r) => p.Value.CompareTo(r.Value));

            return Ranking;
        }


        public List<Movie> Top(int numberOfMovies)
        {
            var movies = db.Movies.Include(m => m.Genre);
            List<KeyValuePair<Movie, double>> dictionary = new List<KeyValuePair<Movie, double>>();

            foreach (Movie movie in movies)
            {
                dictionary.Add(new KeyValuePair<Movie, double>(movie, MovieAverageRating(movie)));
            }

            return TopLogic(dictionary, numberOfMovies);
        }

        public List<Movie> TopLogic(List<KeyValuePair<Movie, double>> dictionary, int numberOfMovies)
        {
            var Ranking = dictionary.ToList();
            Ranking.Sort((p, r) => p.Value.CompareTo(r.Value));
            Ranking.OrderBy(s => s.Value);
            Ranking.Reverse();

            var top = new List<Movie>();
            int i = 0;
            foreach (var item in Ranking)
            {
                top.Add(item.Key);
                i++;
                if (i == numberOfMovies)
                    break;
            }

            return top;
        }

        public double MovieAverageRating(Movie movie)
        {
            var ratings = db.Ratings.Where(m => m.MovieID.Equals(movie.ID)).ToList();
            return MovieAverageRatingLogic(ratings);


        }
        public double MovieAverageRatingLogic(List<Rating> ratings)
        {
            if (ratings.Count != 0)
            {
                double rate = 0; int cnt = 0;

                foreach (Rating rating in ratings)
                {
                    rate += rating.Value;
                    cnt++;
                }

                return rate /= cnt;
            }

            return 0;
        }


        public double MovieAverageRating(int movieid)
        {
            var movie = db.Movies.Where(m => m.ID == movieid).First();

            var ratings = db.Ratings.Where(m => m.MovieID.Equals(movie.ID)).ToList();

            if (movie != null && ratings.Count != 0)
            {
                double rate = 0; int cnt = 0;

                foreach (Rating rating in ratings)
                {
                    rate += rating.Value;
                    cnt++;
                }

                return rate /= cnt;
            }

            return 0;
        }

    }
}
