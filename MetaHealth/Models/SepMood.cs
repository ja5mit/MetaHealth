namespace MetaHealth.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;

    public partial class SepMood
    {
        [Key]
        public int PK { get; set; }

        [StringLength(128)]
        public string UserID { get; set; }

        [Range(1, 5)]
        public int MoodNum { get; set; }

        public DateTime Date { get; set; }

        //reason for why the mood is being added
        public string Reason { get; set; }
    }
}